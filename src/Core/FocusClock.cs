namespace Pulso;

public sealed record Routine
{
    public Guid Id { get; init; } = Guid.NewGuid();
    public string Name { get; init; } = "Foco agendado";
    public int Days { get; init; } = 62; // Domingo é o bit 0, segunda é o bit 1.
    public int StartMinute { get; init; } = 540;
    public int Minutes { get; init; } = 25;
    public int? HourMinutes { get; init; }
    public bool Enabled { get; init; } = true;
    public DateOnly? LastOccurrence { get; init; }
}

public sealed record Session(string Title, DateTimeOffset Start, DateTimeOffset End, bool Scheduled)
{
    public Guid? RoutineId { get; init; }
}
public sealed record Entry(string Title, DateTimeOffset Start, DateTimeOffset End, bool Completed);
public sealed record WindowOfFocus(Routine Routine, DateOnly Day, DateTimeOffset Start, DateTimeOffset End);
public sealed record SavedAppInfo(string DisplayName, string CategoryId);
public sealed record FocusData
{
    public string Theme { get; init; } = "system";
    public int Minutes { get; init; } = 25;
    public string[] Apps { get; init; } = [];
    public Dictionary<string, SavedAppInfo> AppInfo { get; init; } = new(StringComparer.OrdinalIgnoreCase);
    public string[] Sites { get; init; } = [];
    public Routine[] Routines { get; init; } = [];
    public Entry[] History { get; init; } = [];
    public Session? Active { get; init; }
    public UsageState Usage { get; init; } = new();
}

public static class FocusClock
{
    public static FocusData Start(FocusData data, DateTimeOffset now, int minutes)
    {
        if (data.Active != null) return data;
        if (minutes is < 1 or > 720) throw new ArgumentOutOfRangeException(nameof(minutes));
        return data with { Minutes = minutes, Active = new("Foco rápido", now, now.AddMinutes(minutes), false) };
    }

    public static FocusData Tick(FocusData data, DateTimeOffset now, TimeZoneInfo zone)
    {
        if (data.Active is { } finished && finished.End <= now) data = Finish(data, finished.End, true);
        if (data.Active is { Scheduled: true } scheduled && now >= scheduled.Start)
        {
            var id = scheduled.RoutineId ?? Nearby(data, scheduled.Start, zone).FirstOrDefault(w =>
                w.Day == w.Routine.LastOccurrence && w.Routine.Name == scheduled.Title && w.End == scheduled.End)?.Routine.Id;
            if (id.HasValue)
            {
                var window = Nearby(data, now, zone).FirstOrDefault(w => w.Routine.Id == id && !w.Routine.HourMinutes.HasValue
                    && w.Start <= now && now < w.End);
                if (window == null) data = Finish(data, now, false);
                else if (scheduled.RoutineId != id || scheduled.End != window.End || scheduled.Title != window.Routine.Name)
                    data = data with { Active = scheduled with { RoutineId = id, End = window.End, Title = window.Routine.Name } };
            }
        }
        if (data.Active != null) return data;
        var due = Occurrences(data.Routines, now, zone, -1, 0)
            .Where(w => !w.Routine.HourMinutes.HasValue && w.Start <= now && now < w.End && w.Day != w.Routine.LastOccurrence)
            .OrderBy(w => w.Start).FirstOrDefault();
        if (due == null) return data;
        return data with
        {
            Active = new(due.Routine.Name, now, due.End, true) { RoutineId = due.Routine.Id },
            Routines = data.Routines.Select(r => r.Id == due.Routine.Id ? r with { LastOccurrence = due.Day } : r).ToArray()
        };
    }

    public static FocusData Stop(FocusData data, DateTimeOffset now, TimeZoneInfo zone)
    {
        if (data.Active == null) return data;
        var current = Occurrences(data.Routines, now, zone, -1, 0)
            .Where(w => !w.Routine.HourMinutes.HasValue && w.Start <= now && now < w.End).ToDictionary(w => w.Routine.Id);
        var stopped = Finish(data, now, now >= data.Active.End);
        return stopped with { Routines = stopped.Routines.Select(r =>
            current.TryGetValue(r.Id, out var w) ? r with { LastOccurrence = w.Day } : r).ToArray() };
    }

    private static FocusData Finish(FocusData data, DateTimeOffset now, bool completed)
    {
        var s = data.Active!;
        var end = now < s.Start ? s.Start : now > s.End ? s.End : now;
        return data with { Active = null, History = data.History.Prepend(new(s.Title, s.Start, end, completed)).Take(2000).ToArray() };
    }

    public static WindowOfFocus? Next(FocusData data, DateTimeOffset now, TimeZoneInfo zone) =>
        Occurrences(data.Routines, now, zone, 0, 7).Where(w => w.Start > now && (w.Routine.HourMinutes.HasValue || w.Day != w.Routine.LastOccurrence))
            .OrderBy(w => w.Start).FirstOrDefault();

    internal static IEnumerable<WindowOfFocus> Nearby(FocusData data, DateTimeOffset now, TimeZoneInfo zone) =>
        Occurrences(data.Routines, now, zone, -1, 0);

    private static IEnumerable<WindowOfFocus> Occurrences(Routine[] routines, DateTimeOffset now, TimeZoneInfo zone, int first, int last)
    {
        var day = DateOnly.FromDateTime(TimeZoneInfo.ConvertTime(now, zone).DateTime);
        foreach (var r in routines.Where(r => r.Enabled))
        for (var offset = first; offset <= last; offset++)
        {
            var date = day.AddDays(offset);
            if ((r.Days & (1 << (int)date.DayOfWeek)) == 0) continue;
            var wall = date.ToDateTime(TimeOnly.MinValue).AddMinutes(r.StartMinute);
            if (zone.IsInvalidTime(wall)) continue;
            var start = new DateTimeOffset(wall, zone.GetUtcOffset(wall));
            yield return new(r, date, start, start.AddMinutes(r.Minutes));
        }
    }

    public static TimeSpan Today(FocusData data, DateTimeOffset now, TimeZoneInfo zone)
    {
        var local = TimeZoneInfo.ConvertTime(now, zone).Date;
        var dayStart = new DateTimeOffset(local, zone.GetUtcOffset(local));
        double seconds = 0;
        void Add(DateTimeOffset start, DateTimeOffset end)
        {
            if (end > now) end = now;
            if (start < dayStart) start = dayStart;
            if (end > start) seconds += (end - start).TotalSeconds;
        }
        foreach (var entry in data.History) Add(entry.Start, entry.End);
        if (data.Active is { } active) Add(active.Start, active.End);
        return TimeSpan.FromSeconds(seconds);
    }
}
