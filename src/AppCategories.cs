namespace Pulso;

internal static class AppCategories
{
    private static readonly Dictionary<string, (string Label, int Order)> Categories = new(StringComparer.OrdinalIgnoreCase);
    private static readonly Dictionary<string, string> Processes = new(StringComparer.OrdinalIgnoreCase);

    static AppCategories()
    {
        using var table = typeof(AppCategories).Assembly.GetManifestResourceStream("Pulso.AppCategories.tsv");
        if (table != null)
        {
            using var reader = new StreamReader(table);
            while (reader.ReadLine() is { } line)
            {
                if (line.TrimStart().StartsWith('#')) continue;
                var fields = line.Split('\t', StringSplitOptions.TrimEntries);
                if (fields.Length != 4 || fields[0].Length == 0 || fields[1].Length == 0) continue;
                Categories.TryAdd(fields[0], (fields[1], Categories.Count));
                foreach (var process in fields[2].Split('|', StringSplitOptions.TrimEntries | StringSplitOptions.RemoveEmptyEntries))
                    if (process != "-") Processes.TryAdd(process, fields[0]);
            }
        }
        Categories.TryAdd("other", ("Outros", Categories.Count));
    }

    internal static string ForWindows(string processName, string? displayName = null, string? path = null)
    {
        string process = processName.Trim();
        if (process.EndsWith(".exe", StringComparison.OrdinalIgnoreCase)) process = process[..^4];
        if (Processes.TryGetValue(process, out var category)) return category;
        if (displayName != null && Processes.TryGetValue(displayName.Trim(), out category)) return category;
        if (path != null)
        {
            string windowsPath = path.Replace('/', '\\');
            if (windowsPath.Contains(@"\steamapps\common\", StringComparison.OrdinalIgnoreCase) ||
                windowsPath.Contains(@"\GOG Games\", StringComparison.OrdinalIgnoreCase) ||
                windowsPath.Contains(@"\XboxGames\", StringComparison.OrdinalIgnoreCase)) return "games";
        }
        return "other";
    }

    private static (string Label, int Order) Category(string? id) =>
        id != null && Categories.TryGetValue(id.Trim(), out var category) ? category : Categories["other"];

    internal static string Label(string id) => Category(id).Label;
    internal static int Order(string id) => Category(id).Order;
}
