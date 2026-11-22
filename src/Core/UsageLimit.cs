using System.Text.Json.Serialization;

namespace Pulso;

public sealed record UsageLimit(int? HourMinutes = null, int? DayMinutes = null, long HourTicks = 0, long DayTicks = 0)
{
    [JsonIgnore] public bool Enabled => HourMinutes.HasValue || DayMinutes.HasValue;
    internal long AvailableTicks => Math.Min(Remaining(HourMinutes, HourTicks), Remaining(DayMinutes, DayTicks));

    private static long Remaining(int? minutes, long used) =>
        minutes.HasValue ? Math.Max(0, minutes.Value * TimeSpan.TicksPerMinute - used) : long.MaxValue;

    internal UsageLimit Charge(long ticks) => this with
    {
        HourTicks = HourMinutes.HasValue ? HourTicks + ticks : 0,
        DayTicks = DayMinutes.HasValue ? DayTicks + ticks : 0
    };

    internal UsageLimit WithMinutes(int? hour, int? day) => new(hour, day,
        HourMinutes.HasValue && hour.HasValue ? HourTicks : 0,
        DayMinutes.HasValue && day.HasValue ? DayTicks : 0);

    internal UsageLimit Reset(bool hour, bool day) =>
        this with { HourTicks = hour ? 0 : HourTicks, DayTicks = day ? 0 : DayTicks };

    internal UsageLimit Clean()
    {
        int? hour = HourMinutes is >= 1 and <= 60 ? HourMinutes : null;
        int? day = DayMinutes is >= 1 and <= 1440 ? DayMinutes : null;
        return new(hour, day, hour.HasValue ? Math.Clamp(HourTicks, 0, TimeSpan.TicksPerHour) : 0,
            day.HasValue ? Math.Clamp(DayTicks, 0, TimeSpan.TicksPerDay) : 0);
    }

    internal DateTimeOffset? BlockedUntil(UsageState state, UsagePeriod period)
    {
        DateTimeOffset? end = null;
        if (state.HourStart == period.HourStart && Remaining(HourMinutes, HourTicks) == 0) end = period.HourEnd;
        if (state.Day == period.Day && Remaining(DayMinutes, DayTicks) == 0 && (end == null || period.DayEnd > end))
            end = period.DayEnd;
        return end;
    }
}

public sealed record UsageState
{
    public UsageLimit Shared { get; init; } = new();
    public Dictionary<string, UsageLimit> Items { get; init; } = new();
    public DateTimeOffset? HourStart { get; init; }
    public DateOnly? Day { get; init; }
    [JsonIgnore] public bool Enabled => Shared.Enabled || Items.Count != 0;
}

internal readonly record struct UsagePeriod(DateTimeOffset HourStart, DateTimeOffset HourEnd, DateOnly Day, DateTimeOffset DayEnd)
{
    public static UsagePeriod At(DateTimeOffset now, TimeZoneInfo zone)
    {
        var local = TimeZoneInfo.ConvertTime(now, zone);
        var day = DateOnly.FromDateTime(local.DateTime);
        var hourStart = new DateTimeOffset(local.Date.AddHours(local.Hour), local.Offset);
        var hourEnd = hourStart.AddHours(1);
        // Offsets can also move by half an hour. The old clock hour ends at the actual transition.
        if (zone.GetUtcOffset(hourEnd) != local.Offset)
        {
            long low = now.UtcTicks, high = hourEnd.UtcTicks;
            while (high - low > 1)
            {
                long middle = low + (high - low) / 2;
                if (zone.GetUtcOffset(new DateTimeOffset(middle, TimeSpan.Zero)) == local.Offset) low = middle;
                else high = middle;
            }
            hourEnd = new DateTimeOffset(high, TimeSpan.Zero);
        }
        var midnight = day.AddDays(1).ToDateTime(TimeOnly.MinValue);
        while (zone.IsInvalidTime(midnight)) midnight = midnight.AddMinutes(1);
        var offset = zone.IsAmbiguousTime(midnight) ? zone.GetAmbiguousTimeOffsets(midnight).Max() : zone.GetUtcOffset(midnight);
        return new(hourStart, hourEnd, day, new(midnight, offset));
    }
}
