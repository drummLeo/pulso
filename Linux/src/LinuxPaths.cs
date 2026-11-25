namespace Pulso;

internal static class LinuxPaths
{
    public static string HomeDirectory() =>
        Environment.GetFolderPath(Environment.SpecialFolder.UserProfile) is { Length: > 0 } profile
            ? profile
            : Environment.GetEnvironmentVariable("HOME") ?? "/tmp";

    public static string DataFolder(string? custom = null, Func<string, string?>? getEnv = null, string? home = null)
    {
        if (!string.IsNullOrWhiteSpace(custom)) return Path.GetFullPath(custom.Trim());
        var env = getEnv ?? Environment.GetEnvironmentVariable;
        var dataHome = env("XDG_DATA_HOME");
        if (!string.IsNullOrWhiteSpace(dataHome) && Path.IsPathRooted(dataHome))
            return Path.Combine(Path.GetFullPath(dataHome), "PulsoFoco");
        return Path.Combine(home ?? HomeDirectory(), ".local", "share", "PulsoFoco");
    }

    public static IReadOnlyList<string> ApplicationDirectories(string? xdgDataHome = null, string? xdgDataDirs = null, string? home = null)
    {
        var result = new List<string>();
        var seen = new HashSet<string>(StringComparer.Ordinal);

        void AddBase(string? dir)
        {
            if (string.IsNullOrWhiteSpace(dir) || !Path.IsPathRooted(dir)) return;
            var appsDir = Path.GetFullPath(Path.Combine(dir, "applications")).TrimEnd(Path.DirectorySeparatorChar);
            if (appsDir.Length == 0) appsDir = "/applications";
            if (seen.Add(appsDir)) result.Add(appsDir);
        }

        string? dataHome = xdgDataHome ?? Environment.GetEnvironmentVariable("XDG_DATA_HOME");
        if (!string.IsNullOrWhiteSpace(dataHome) && Path.IsPathRooted(dataHome)) AddBase(dataHome);
        else AddBase(Path.Combine(home ?? HomeDirectory(), ".local", "share"));

        string? dataDirs = xdgDataDirs ?? Environment.GetEnvironmentVariable("XDG_DATA_DIRS");
        if (string.IsNullOrWhiteSpace(dataDirs)) dataDirs = "/usr/local/share:/usr/share";
        foreach (var entry in dataDirs.Split(Path.PathSeparator, StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
            AddBase(entry);

        return result;
    }

    public static string RuntimeDirectory(Func<string, string?>? getEnv = null)
    {
        var env = getEnv ?? Environment.GetEnvironmentVariable;
        var runtime = env("XDG_RUNTIME_DIR");
        if (!string.IsNullOrWhiteSpace(runtime) && Path.IsPathRooted(runtime) && Directory.Exists(runtime))
            return Path.GetFullPath(runtime);
        return Path.GetTempPath();
    }
}
