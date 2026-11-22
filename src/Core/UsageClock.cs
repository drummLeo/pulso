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
        var scheduleTicks = new Dictionary<Guid, long>();
        foreach (var routine in data.Routines.Where(r => r.HourMinutes.HasValue))
        {
            long used = state.ScheduleTicks?.GetValueOrDefault(routine.Id) ?? 0;
            if (used > 0) scheduleTicks[routine.Id] = Math.Min(used, TimeSpan.TicksPerHour);
        }
        return data with { Usage = state with { Shared = state.Shared?.Clean() ?? Off, Items = items, ScheduleTicks = scheduleTicks } };
    }

    public static string[] WatchedSites(FocusData data) =>
        WatchedSites(data, DateTimeOffset.Now, TimeZoneInfo.Local);

    public static string[] WatchedSites(FocusData data, DateTimeOffset now, TimeZoneInfo zone) =>
        WatchedSites(data, new UsagePolicy(data, now, zone).ActiveQuotas.Length != 0);

    internal static string[] WatchedSites(FocusData data, bool activeQuota) =>
        data.Usage.Shared.Enabled || activeQuota ? data.Sites : data.Sites.Where(d => data.Usage.Items.ContainsKey(SiteKey(d))).ToArray();

    internal static bool HasLimits(FocusData data) =>
        data.Usage.Enabled || data.Routines.Any(r => r.Enabled && r.HourMinutes.HasValue);

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
                Items = state.Items.ToDictionary(pair => pair.Key, pair => pair.Value.Reset(hour, day)),
                ScheduleTicks = hour ? new() : state.ScheduleTicks
            }
        };
    }

    public static FocusData Advance(FocusData data, DateTimeOffset now, TimeZoneInfo zone) =>
        HasLimits(data) ? Advance(data, UsagePeriod.At(now, zone)) : data;

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
        if (!HasLimits(data)) return data;
        var keys = MatchingKeys(data, processName, host).ToArray();
        if (keys.Length == 0) return Advance(data, to, zone);
        for (var cursor = from; cursor < to;)
        {
            var period = UsagePeriod.At(cursor, zone);
            data = Advance(data, period);
            var end = period.HourEnd < to ? period.HourEnd : to;
            if (period.DayEnd < end) end = period.DayEnd;
            var quotas = new List<WindowOfFocus>();
            bool paused = false;
            foreach (var window in FocusClock.Nearby(data, cursor, zone))
            {
                if (!window.Routine.HourMinutes.HasValue && window.Day == window.Routine.LastOccurrence) continue;
                if (window.Start > cursor)
                {
                    if (window.Start < end) end = window.Start;
                }
                else if (cursor < window.End)
                {
                    if (window.End < end) end = window.End;
                    if (window.Routine.HourMinutes.HasValue) quotas.Add(window);
                    else paused = true;
                }
            }
            if (data.Active is { } focus)
            {
                if (cursor >= focus.Start && cursor < focus.End)
                {
                    if (focus.End < end) end = focus.End;
                    paused = true;
                }
                if (cursor < focus.Start && focus.Start < end) end = focus.Start;
            }
            if (!paused) data = Charge(data, keys, quotas, (end - cursor).Ticks);
            cursor = end;
        }
        return Advance(data, to, zone);
    }

    private static FocusData Charge(FocusData data, string[] keys, List<WindowOfFocus> quotas, long ticks)
    {
        var state = data.Usage;
        ticks = Math.Min(ticks, state.Shared.AvailableTicks);
        foreach (string key in keys)
            if (state.Items.TryGetValue(key, out var limit)) ticks = Math.Min(ticks, limit.AvailableTicks);
        foreach (var window in quotas)
            ticks = Math.Min(ticks, window.Routine.HourMinutes!.Value * TimeSpan.TicksPerMinute - state.ScheduleTicks.GetValueOrDefault(window.Routine.Id));
        if (ticks <= 0) return data;
        Dictionary<string, UsageLimit>? updated = null;
        foreach (string key in keys)
        {
            if (!state.Items.TryGetValue(key, out var limit)) continue;
            updated ??= new(state.Items);
            updated[key] = limit.Charge(ticks);
        }
        if (updated == null && !state.Shared.Enabled && quotas.Count == 0) return data;
        var scheduleTicks = state.ScheduleTicks;
        if (quotas.Count > 0)
        {
            scheduleTicks = new(scheduleTicks);
            foreach (var window in quotas)
                scheduleTicks[window.Routine.Id] = scheduleTicks.GetValueOrDefault(window.Routine.Id) + ticks;
        }
        return data with { Usage = state with
        {
            Shared = state.Shared.Enabled ? state.Shared.Charge(ticks) : state.Shared,
            Items = updated ?? state.Items, ScheduleTicks = scheduleTicks
        } };
    }

    public static DateTimeOffset? BlockedUntil(FocusData data, string? key, DateTimeOffset now, TimeZoneInfo zone)
    {
        if (key == null) return data.Usage.Shared.BlockedUntil(data.Usage, UsagePeriod.At(now, zone));
        return IsListed(data, key) ? new UsagePolicy(data, now, zone).LimitFor(key)?.Until : null;
    }

    public static UsageBlock? BlockFor(FocusData data, string key, DateTimeOffset now, TimeZoneInfo zone) =>
        IsListed(data, key) ? new UsagePolicy(data, now, zone).BlockFor(key) : null;

    private static bool IsListed(FocusData data, string key) =>
        key.StartsWith(SitePrefix, StringComparison.Ordinal)
            ? SiteDomains(key[SitePrefix.Length..]).Any(domain => data.Sites.Contains(domain, StringComparer.OrdinalIgnoreCase))
            : data.Apps.Any(name => AppKey(name) == key);

    internal static DateTimeOffset? IndividualEnd(FocusData data, string key, UsagePeriod period)
    {
        var state = data.Usage;
        if (!key.StartsWith(SitePrefix, StringComparison.Ordinal))
            return Get(data, key).BlockedUntil(state, period);
        DateTimeOffset? end = null;
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
