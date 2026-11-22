namespace Pulso;

public static class UsageClock
{
    private static readonly UsageLimit Off = new();
    private const string SitePrefix = "site:";
    public static string AppKey(string name) => "app:" + name.ToLowerInvariant();
    public static string SiteKey(string domain) => SitePrefix + domain.ToLowerInvariant();

    public static UsageLimit Get(FocusData data, string? key) =>
        key == null ? data.Usage.Shared : data.Usage.Items.GetValueOrDefault(key, Off);

    public static FocusData Configure(FocusData data, string? key, int? hour, int? day)
    {
        if (hour is < 1 or > 60) throw new ArgumentOutOfRangeException(nameof(hour));
        if (day is < 1 or > 1440) throw new ArgumentOutOfRangeException(nameof(day));
        if (key != null && !ListKeys(data).Contains(key)) throw new ArgumentException("O item precisa estar na lista.", nameof(key));
        var current = Get(data, key);
        var limit = current.WithMinutes(hour, day);
        if (current == limit) return data;
        var state = data.Usage;
        if (key == null) return data with { Usage = state with { Shared = limit } };
        var items = new Dictionary<string, UsageLimit>(state.Items);
        if (limit.Enabled) items[key] = limit;
        else items.Remove(key);
        return data with { Usage = state with { Items = items } };
    }

    private static IEnumerable<string> ListKeys(FocusData data) =>
        data.Apps.Select(AppKey).Concat(data.Sites.Select(SiteKey));

    public static FocusData Reconcile(FocusData data)
    {
        var state = data.Usage ?? new();
        var items = new Dictionary<string, UsageLimit>();
        foreach (string key in ListKeys(data))
            if (state.Items?.GetValueOrDefault(key)?.Clean() is { Enabled: true } limit) items[key] = limit;
        return data with { Usage = state with { Shared = state.Shared?.Clean() ?? Off, Items = items } };
    }

    public static string[] WatchedSites(FocusData data) =>
        data.Usage.Shared.Enabled ? data.Sites : data.Sites.Where(d => data.Usage.Items.ContainsKey(SiteKey(d))).ToArray();

    private static FocusData Advance(FocusData data, UsagePeriod period)
    {
        var state = data.Usage;
        bool hour = state.HourStart != period.HourStart, day = state.Day != period.Day;
        if (!hour && !day) return data;
        return data with
        {
            Usage = state with
            {
                HourStart = period.HourStart, Day = period.Day, Shared = state.Shared.Reset(hour, day),
                Items = state.Items.ToDictionary(pair => pair.Key, pair => pair.Value.Reset(hour, day))
            }
        };
    }

    public static FocusData Advance(FocusData data, DateTimeOffset now, TimeZoneInfo zone) =>
        data.Usage.Enabled ? Advance(data, UsagePeriod.At(now, zone)) : data;

    private static IEnumerable<string> SiteDomains(string host)
    {
        while (host.Contains('.'))
        {
            yield return host;
            host = host[(host.IndexOf('.') + 1)..];
        }
    }

    private static IEnumerable<string> MatchingKeys(FocusData data, string? processName, string? host)
    {
        if (processName != null && data.Apps.Contains(processName, StringComparer.OrdinalIgnoreCase)) yield return AppKey(processName);
        if (host == null) yield break;
        foreach (string domain in SiteDomains(host))
            if (data.Sites.Contains(domain, StringComparer.OrdinalIgnoreCase)) yield return SiteKey(domain);
    }

    // App and matching site(s) may each have their own allowance, while all share a single debit.
    public static FocusData Accrue(FocusData data, string? processName, string? host, DateTimeOffset from, DateTimeOffset to, TimeZoneInfo zone)
    {
        if (!data.Usage.Enabled) return data;
        var keys = MatchingKeys(data, processName, host).ToArray();
        if (keys.Length == 0) return Advance(data, to, zone);
        for (var cursor = from; cursor < to;)
        {
            var period = UsagePeriod.At(cursor, zone);
            data = Advance(data, period);
            var end = period.HourEnd < to ? period.HourEnd : to;
            if (period.DayEnd < end) end = period.DayEnd;
            if (data.Active is { } focus)
            {
                if (cursor >= focus.Start && cursor < focus.End)
                {
                    cursor = focus.End < end ? focus.End : end;
                    continue;
                }
                if (cursor < focus.Start && focus.Start < end) end = focus.Start;
            }
            data = Charge(data, keys, (end - cursor).Ticks);
            cursor = end;
        }
        return Advance(data, to, zone);
    }

    private static FocusData Charge(FocusData data, string[] keys, long ticks)
    {
        var state = data.Usage;
        ticks = Math.Min(ticks, state.Shared.AvailableTicks);
        foreach (string key in keys)
            if (state.Items.TryGetValue(key, out var limit)) ticks = Math.Min(ticks, limit.AvailableTicks);
        if (ticks == 0) return data;
        Dictionary<string, UsageLimit>? updated = null;
        foreach (string key in keys)
        {
            if (!state.Items.TryGetValue(key, out var limit)) continue;
            updated ??= new(state.Items);
            updated[key] = limit.Charge(ticks);
        }
        if (updated == null && !state.Shared.Enabled) return data;
        return data with { Usage = state with { Shared = state.Shared.Enabled ? state.Shared.Charge(ticks) : state.Shared, Items = updated ?? state.Items } };
    }

    public static DateTimeOffset? BlockedUntil(FocusData data, string? key, DateTimeOffset now, TimeZoneInfo zone)
    {
        if (!data.Usage.Enabled) return null;
        if (key != null)
        {
            bool listed = key.StartsWith(SitePrefix, StringComparison.Ordinal)
                ? SiteDomains(key[SitePrefix.Length..]).Any(domain => data.Sites.Contains(domain, StringComparer.OrdinalIgnoreCase))
                : data.Apps.Any(name => AppKey(name) == key);
            if (!listed) return null;
        }
        return BlockedUntil(data, key, UsagePeriod.At(now, zone));
    }

    internal static DateTimeOffset? BlockedUntil(FocusData data, string? key, UsagePeriod period)
    {
        var state = data.Usage;
        var end = state.Shared.BlockedUntil(state, period);
        if (key == null) return end;
        if (!key.StartsWith(SitePrefix, StringComparison.Ordinal))
            return Later(end, Get(data, key).BlockedUntil(state, period));
        foreach (var domain in SiteDomains(key[SitePrefix.Length..]))
            if (state.Items.TryGetValue(SiteKey(domain), out var limit)) end = Later(end, limit.BlockedUntil(state, period));
        return end;
    }

    internal static DateTimeOffset? Later(DateTimeOffset? left, DateTimeOffset? right) =>
        !right.HasValue || left >= right ? left : right;
}

public sealed class UsageMeter
{
    private DateTimeOffset? previous;
    private TimeSpan previousElapsed;
    private string? previousApp, previousSite;

    public void Reset() => previous = null;

    public FocusData Sample(FocusData data, DateTimeOffset now, TimeSpan elapsed, string? app, string? site, TimeZoneInfo zone)
    {
        if (previous.HasValue && app != null && string.Equals(app, previousApp, StringComparison.OrdinalIgnoreCase)
            && string.Equals(site, previousSite, StringComparison.OrdinalIgnoreCase))
        {
            var wall = now - previous.Value;
            var uptime = elapsed - previousElapsed;
            // A late timer, suspend or clock adjustment cannot establish foreground use for the gap.
            if (wall > TimeSpan.Zero && uptime > TimeSpan.Zero && uptime <= TimeSpan.FromSeconds(3)
                && (wall - uptime).Duration() < TimeSpan.FromMilliseconds(250))
            {
                var observed = wall < uptime ? wall : uptime;
                data = UsageClock.Accrue(data, app, site, now - observed, now, zone);
            }
        }
        previous = now;
        previousElapsed = elapsed;
        previousApp = app;
        previousSite = site;
        return UsageClock.Advance(data, now, zone);
    }
}
