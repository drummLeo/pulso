namespace Pulso;

public enum UsageBlockKind { Focus, Schedule, Shared, Item }
public sealed record UsageBlock(UsageBlockKind Kind, string Title, DateTimeOffset Until);

// One view of the current local time and rules for all listed apps and domains.
internal sealed class UsagePolicy
{
    private readonly FocusData data;
    private readonly UsageBlock? listLimit;
    public UsagePeriod Period { get; }
    public WindowOfFocus[] ActiveQuotas { get; }
    public UsageBlock? Focus { get; }
    public UsageBlock? Shared { get; }
    public SiteFilterPolicy Filters { get; }

    public UsagePolicy(FocusData data, DateTimeOffset now, TimeZoneInfo zone)
    {
        this.data = data;
        Period = UsagePeriod.At(now, zone);
        if (data.Active is { } session && session.Start <= now && now < session.End)
            Focus = new(UsageBlockKind.Focus, session.Title, session.End);
        if (data.Usage.Shared.BlockedUntil(data.Usage, Period) is { } until)
            Shared = new(UsageBlockKind.Shared, "Limite compartilhado", until);
        var quotas = new List<WindowOfFocus>();
        DateTimeOffset? quotaEnd = null;
        foreach (var window in FocusClock.Nearby(data, now, zone).Where(w => w.Start <= now && now < w.End))
        {
            if (window.Routine.HourMinutes.HasValue)
            {
                quotas.Add(window);
                quotaEnd = UsageClock.Later(quotaEnd, window.End);
                if (ScheduleEnd(window) is { } end)
                    listLimit = Longest(listLimit, new(UsageBlockKind.Schedule, window.Routine.Name, end));
            }
            else if (window.Day != window.Routine.LastOccurrence)
                Focus = Longest(Focus, new(UsageBlockKind.Focus, window.Routine.Name, window.End));
        }
        ActiveQuotas = quotas.ToArray();
        Filters = SiteFilterPolicy.At(data, UsageClock.Later(Focus?.Until, quotaEnd));
        listLimit = Longest(listLimit, Shared);
    }

    public long ScheduleUsed(Guid id) => data.Usage.HourStart == Period.HourStart ? data.Usage.ScheduleTicks.GetValueOrDefault(id) : 0;

    public DateTimeOffset? ScheduleEnd(WindowOfFocus window) =>
        ScheduleUsed(window.Routine.Id) >= window.Routine.HourMinutes!.Value * TimeSpan.TicksPerMinute
            ? (window.End < Period.HourEnd ? window.End : Period.HourEnd) : null;

    public UsageBlock? LimitFor(string key) => UsageClock.IndividualEnd(data, key, Period) is { } end
        ? Longest(listLimit, new(UsageBlockKind.Item, "Limite individual", end)) : listLimit;

    public UsageBlock? BlockFor(string key) => Longest(Focus, LimitFor(key));

    private static UsageBlock? Longest(UsageBlock? first, UsageBlock? other) =>
        other != null && (first == null || other.Until > first.Until) ? other : first;
}
