using System.Text.Json;

namespace Pulso;

public sealed class FocusStore(string folder)
{
    private readonly string path = Path.Combine(folder, "foco.json");
    private bool recovered;
    private static readonly JsonSerializerOptions Json = new() { WriteIndented = true };

    public FocusData Load()
    {
        if (!File.Exists(path) && !File.Exists(path + ".bak")) return new();
        foreach (string candidate in new[] { path, path + ".bak" })
        {
            if (!File.Exists(candidate)) continue;
            try
            {
                using var file = new FileStream(candidate, FileMode.Open, FileAccess.Read, FileShare.Read | FileShare.Delete);
                var data = JsonSerializer.Deserialize<FocusData>(file);
                if (data == null || data.Apps == null || data.Sites == null || data.Routines == null || data.History == null
                    || data.Routines.Any(r => r == null || r.StartMinute is < 0 or >= 1440 || r.Minutes is < 1 or > 720
                        || r.Days is < 1 or > 127 || r.HourMinutes is < 1 or > 60)
                    || data.Routines.Select(r => r.Id).Distinct().Count() != data.Routines.Length
                    || data.History.Any(e => e == null || e.End < e.Start)
                    || data.Active is { } active && active.End <= active.Start) continue;
                recovered = candidate != path;
                var apps = data.Apps.Select(Targets.App).OfType<string>().Distinct(StringComparer.OrdinalIgnoreCase).ToArray();
                var selected = apps.ToHashSet(StringComparer.OrdinalIgnoreCase);
                var appInfo = new Dictionary<string, SavedAppInfo>(StringComparer.OrdinalIgnoreCase);
                foreach (var (key, info) in data.AppInfo ?? [])
                    if (Targets.App(key) is { } name && selected.Contains(name) && !string.IsNullOrWhiteSpace(info?.DisplayName)
                        && !string.IsNullOrWhiteSpace(info.CategoryId))
                        appInfo[name] = info;
                return UsageClock.Reconcile(data with
                {
                    Theme = data.Theme is "light" or "dark" ? data.Theme : "system",
                    Minutes = Math.Clamp(data.Minutes, 1, 720),
                    Apps = apps, AppInfo = appInfo,
                    Sites = data.Sites.Select(Targets.Domain).OfType<string>().Distinct().ToArray()
                });
            }
            catch (JsonException) { }
        }
        throw new InvalidDataException("Não foi possível ler as configurações salvas em " + folder + ".");
    }

    public void Save(FocusData data)
    {
        Directory.CreateDirectory(folder);
        string temp = path + "." + Guid.NewGuid().ToString("N") + ".tmp";
        try
        {
            using (var file = new FileStream(temp, FileMode.CreateNew, FileAccess.Write, FileShare.None))
            {
                JsonSerializer.Serialize(file, data, Json);
                file.Flush(true);
            }
            if (File.Exists(path) && !recovered) File.Replace(temp, path, path + ".bak");
            else File.Move(temp, path, true);
            recovered = false;
        }
        finally { if (File.Exists(temp)) File.Delete(temp); }
    }
}
