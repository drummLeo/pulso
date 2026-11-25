using System.Globalization;
using System.Security;
using System.Text;
using System.Text.RegularExpressions;

namespace Pulso;

internal sealed record DiscoveredApp(string ProcessName, string DisplayName, string CategoryId);
internal sealed record AppScan(IReadOnlyList<DiscoveredApp> Apps, bool Partial);

internal static class LinuxInstalledApps
{
    internal static AppScan Scan(CancellationToken token)
    {
        token.ThrowIfCancellationRequested();
        var dirs = LinuxPaths.ApplicationDirectories();
        var catalog = new LinuxInstalledAppCatalog(token);
        bool anyAvailable = false;
        for (int i = 0; i < dirs.Count; i++)
        {
            token.ThrowIfCancellationRequested();
            if (Directory.Exists(dirs[i])) anyAvailable = true;
            catalog.ReadApplicationsDirectory(dirs[i], dirs.Count - i);
        }
        if (!anyAvailable) catalog.MarkPartial();
        return catalog.Result();
    }
}

internal sealed class LinuxInstalledAppCatalog
{
    private readonly CancellationToken token;
    private readonly HashSet<string> currentDesktops;
    private readonly Func<string, bool> commandExists;
    private readonly HashSet<string> seenDesktopIds = new(StringComparer.Ordinal);
    private readonly Dictionary<string, (DiscoveredApp App, int Priority)> apps = new(StringComparer.OrdinalIgnoreCase);
    private bool partial;

    private static readonly StringComparer Titles = StringComparer.Create(CultureInfo.GetCultureInfo("pt-BR"), true);

    private static readonly HashSet<string> SharedProcesses = new(StringComparer.OrdinalIgnoreCase)
    {
        "false", "true", "sh", "bash", "zsh", "fish", "dash", "env",
        "python", "python2", "python3", "java", "javaw", "node", "electron", "ruby", "perl",
        "flatpak", "snap", "xdg-open", "gio", "kioclient", "kioclient5", "kioclient6",
        "gnome-open", "exo-open", "pkexec", "sudo", "gksudo", "kdesudo",
        "dbus-launch", "systemd", "systemctl"
    };

    private static readonly HashSet<string> WrapperLaunchers = new(StringComparer.OrdinalIgnoreCase)
    {
        "gamemoderun", "mangohud", "prime-run", "optirun", "primusrun", "nohup"
    };

    private static readonly Regex Maintenance = new(
        @"^unins(?:t|\d)|^setup|^(?:install|update|maintenance|repair|vc_?redist)(?:[-_. \d]|$)|" +
        @"(?:setup|installer|updater|autoupdate|crashpad[_-]?handler|crashreporter)(?:[-_. ]?(?:x86|x64|arm64|win32|win64|[\d.]+))?$",
        RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);

    private static readonly Regex UriArgument = new(
        @"^(?:[a-z][a-z0-9+.-]*://|mailto:)",
        RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);

    internal LinuxInstalledAppCatalog(CancellationToken token, string? currentDesktop = null, Func<string, bool>? commandExists = null)
    {
        this.token = token;
        string rawDesktop = currentDesktop ?? Environment.GetEnvironmentVariable("XDG_CURRENT_DESKTOP") ?? "";
        currentDesktops = rawDesktop.Split(':', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .ToHashSet(StringComparer.OrdinalIgnoreCase);
        this.commandExists = commandExists ?? CommandExistsInPath;
    }

    internal void MarkPartial() => partial = true;

    internal AppScan Result()
    {
        token.ThrowIfCancellationRequested();
        var sorted = apps.Values.Select(entry => entry.App)
            .OrderBy(app => AppCategories.Order(app.CategoryId))
            .ThenBy(app => app.DisplayName, Titles)
            .ThenBy(app => app.ProcessName, StringComparer.OrdinalIgnoreCase);
        return new(sorted.ToArray(), partial);
    }

    internal void ReadApplicationsDirectory(string folder, int priority)
    {
        token.ThrowIfCancellationRequested();
        if (!Directory.Exists(folder)) return;
        string root = Path.GetFullPath(folder);
        var visitedDirs = new HashSet<string>(StringComparer.Ordinal);
        var stack = new Stack<(string Dir, string Prefix, int Depth)>();
        stack.Push((root, "", 0));

        while (stack.TryPop(out var item))
        {
            token.ThrowIfCancellationRequested();
            if (item.Depth > 8) continue;
            string realDir = ResolveRealPath(item.Dir);
            if (!visitedDirs.Add(realDir)) continue;

            try
            {
                foreach (var entry in new DirectoryInfo(item.Dir).EnumerateFileSystemInfos())
                {
                    token.ThrowIfCancellationRequested();
                    if (entry is DirectoryInfo)
                    {
                        string nextPrefix = item.Prefix.Length == 0 ? entry.Name : item.Prefix + "-" + entry.Name;
                        stack.Push((entry.FullName, nextPrefix, item.Depth + 1));
                    }
                    else if (entry.Extension.Equals(".desktop", StringComparison.OrdinalIgnoreCase))
                    {
                        string desktopId = item.Prefix.Length == 0 ? entry.Name : item.Prefix + "-" + entry.Name;
                        if (!seenDesktopIds.Add(desktopId)) continue;
                        ReadDesktopFile(entry.FullName, desktopId, priority);
                    }
                }
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or SecurityException)
            {
                partial = true;
            }
        }
    }

    internal void ReadDesktopFile(string filePath, string desktopId, int priority)
    {
        token.ThrowIfCancellationRequested();
        seenDesktopIds.Add(desktopId);
        try
        {
            var fields = new Dictionary<string, string>(StringComparer.Ordinal);
            bool inDesktopEntry = false;
            foreach (var rawLine in File.ReadLines(filePath))
            {
                token.ThrowIfCancellationRequested();
                string line = rawLine.Trim();
                if (line.Length == 0 || line[0] == '#') continue;
                if (line[0] == '[' && line[^1] == ']')
                {
                    if (inDesktopEntry) break;
                    inDesktopEntry = line == "[Desktop Entry]";
                    continue;
                }
                if (!inDesktopEntry) continue;
                int eq = line.IndexOf('=');
                if (eq <= 0) continue;
                string key = line[..eq].Trim();
                string value = line[(eq + 1)..].TrimStart();
                fields.TryAdd(key, value);
            }

            if (!fields.TryGetValue("Type", out var type) || !type.Equals("Application", StringComparison.OrdinalIgnoreCase)) return;
            if (IsTrue(fields, "Hidden") || IsTrue(fields, "NoDisplay") || IsTrue(fields, "Terminal")) return;
            if (!MatchesDesktop(fields)) return;

            if (fields.TryGetValue("TryExec", out var rawTryExec) && !string.IsNullOrWhiteSpace(rawTryExec))
            {
                string tryExec = UnescapeDesktopValue(rawTryExec).Trim();
                if (tryExec.Length > 0 && !TryExecExists(tryExec)) return;
            }

            if (!fields.TryGetValue("Exec", out var rawExec) || string.IsNullOrWhiteSpace(rawExec)) return;
            string? wmClass = fields.TryGetValue("StartupWMClass", out var rawWm) ? UnescapeDesktopValue(rawWm).Trim() : null;
            if (!TryExtractProcess(UnescapeDesktopValue(rawExec), wmClass, out var processName, out var resolvedPath)) return;

            string? displayName = FirstNonEmpty(fields, "Name[pt_BR]", "Name[pt]", "Name");
            if (displayName != null) displayName = UnescapeDesktopValue(displayName).Trim();
            if (string.IsNullOrWhiteSpace(displayName) || displayName.Length > 200 || displayName.Any(char.IsControl))
                displayName = processName;

            IEnumerable<string>? categories = fields.TryGetValue("Categories", out var rawCats)
                ? rawCats.Split(';', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
                : null;

            string categoryId = AppCategories.ForLinux(processName, displayName, resolvedPath, categories);
            if (apps.TryGetValue(processName, out var current))
            {
                if (current.Priority > priority) return;
                if (current.Priority == priority && Titles.Compare(displayName, current.App.DisplayName) >= 0) return;
            }
            apps[processName] = (new(processName, displayName, categoryId), priority);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or SecurityException or ArgumentException)
        {
            partial = true;
        }
    }

    private bool MatchesDesktop(Dictionary<string, string> fields)
    {
        if (fields.TryGetValue("OnlyShowIn", out var onlyShow) && !string.IsNullOrWhiteSpace(onlyShow))
        {
            if (currentDesktops.Count == 0) return false;
            var allowed = onlyShow.Split(';', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
            if (!allowed.Any(currentDesktops.Contains)) return false;
        }
        if (fields.TryGetValue("NotShowIn", out var notShow) && !string.IsNullOrWhiteSpace(notShow) && currentDesktops.Count > 0)
        {
            var denied = notShow.Split(';', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
            if (denied.Any(currentDesktops.Contains)) return false;
        }
        return true;
    }

    private bool TryExecExists(string tryExec)
    {
        if (Path.IsPathRooted(tryExec)) return File.Exists(tryExec);
        return commandExists(tryExec);
    }

    private static bool TryExtractProcess(string execLine, string? wmClass, out string processName, out string? resolvedPath)
    {
        processName = "";
        resolvedPath = null;
        var tokens = TokenizeExec(execLine);
        if (tokens.Count == 0) return false;

        int index = 0;
        while (index < tokens.Count)
        {
            string cmd = tokens[index];
            string baseName = Path.GetFileName(cmd);
            if (baseName.Equals("env", StringComparison.OrdinalIgnoreCase))
            {
                index++;
                while (index < tokens.Count)
                {
                    string arg = tokens[index];
                    if (arg == "--") { index++; break; }
                    if (arg is "-u" or "-C" or "-S" or "--unset") { index += 2; continue; }
                    if (arg.StartsWith('-')) { index++; continue; }
                    if (IsEnvAssignment(arg)) { index++; continue; }
                    break;
                }
                continue;
            }
            if (WrapperLaunchers.Contains(baseName)) { index++; continue; }
            if (baseName.Equals("nice", StringComparison.OrdinalIgnoreCase))
            {
                index++;
                if (index < tokens.Count && tokens[index] == "-n") index += 2;
                else if (index < tokens.Count && (tokens[index].StartsWith("-n", StringComparison.Ordinal) || tokens[index].StartsWith("--adjustment=", StringComparison.Ordinal) || (tokens[index].Length > 1 && tokens[index][0] == '-' && char.IsDigit(tokens[index][1]))))
                    index++;
                continue;
            }
            if (baseName.Equals("ionice", StringComparison.OrdinalIgnoreCase))
            {
                index++;
                while (index < tokens.Count && tokens[index].StartsWith('-'))
                {
                    if (tokens[index] is "-c" or "-n" or "-p") index += 2;
                    else index++;
                }
                continue;
            }
            if (baseName.Equals("taskset", StringComparison.OrdinalIgnoreCase))
            {
                index++;
                while (index < tokens.Count && tokens[index].StartsWith('-'))
                {
                    if (tokens[index] is "-c" or "--cpu-list") index += 2;
                    else index++;
                }
                if (index < tokens.Count && IsHexMask(tokens[index])) index++;
                continue;
            }
            if (baseName.Equals("firejail", StringComparison.OrdinalIgnoreCase))
            {
                index++;
                while (index < tokens.Count && tokens[index].StartsWith('-'))
                {
                    bool stop = tokens[index] == "--";
                    index++;
                    if (stop) break;
                }
                continue;
            }
            if (baseName.Equals("bwrap", StringComparison.OrdinalIgnoreCase))
            {
                int sep = tokens.FindIndex(index + 1, t => t == "--");
                if (sep >= 0 && sep + 1 < tokens.Count) { index = sep + 1; continue; }
                return false;
            }
            break;
        }

        if (index >= tokens.Count) return false;

        string targetCmd = tokens[index];
        string targetBase = Path.GetFileName(targetCmd);
        var remainingArgs = tokens.Skip(index + 1).ToList();

        if (targetBase.Equals("flatpak", StringComparison.OrdinalIgnoreCase) && remainingArgs.Count >= 2 && remainingArgs[0] == "run")
        {
            return TryExtractFlatpak(remainingArgs.Skip(1), wmClass, out processName, out resolvedPath);
        }
        if (targetBase.Equals("snap", StringComparison.OrdinalIgnoreCase) && remainingArgs.Count >= 2 && remainingArgs[0] == "run")
        {
            return TryExtractSnap(remainingArgs.Skip(1), wmClass, out processName, out resolvedPath);
        }

        if (HasProtocolOrAppArgument(remainingArgs)) return false;

        if (Path.IsPathRooted(targetCmd) || targetCmd.Contains('/')) resolvedPath = targetCmd;
        string? candidate = Targets.App(targetBase);
        if (candidate == null || SharedProcesses.Contains(candidate) || Maintenance.IsMatch(candidate)) return false;
        processName = candidate;
        return true;
    }

    private static bool TryExtractFlatpak(IEnumerable<string> args, string? wmClass, out string processName, out string? resolvedPath)
    {
        processName = "";
        resolvedPath = null;
        string? command = null;
        string? appId = null;
        var extraArgs = new List<string>();
        bool pastApp = false;
        foreach (var arg in args)
        {
            if (pastApp)
            {
                extraArgs.Add(arg);
                continue;
            }
            if (arg.StartsWith("--command=", StringComparison.Ordinal))
            {
                command = arg["--command=".Length..];
                continue;
            }
            if (arg.StartsWith('-')) continue;
            if (arg.StartsWith("@@", StringComparison.Ordinal)) continue;
            appId = arg;
            pastApp = true;
        }
        if (HasProtocolOrAppArgument(extraArgs)) return false;

        string? cmdBase = command != null ? Path.GetFileName(command) : null;
        if (command != null && Path.IsPathRooted(command)) resolvedPath = command;

        string? chosen = null;
        if (!string.IsNullOrWhiteSpace(cmdBase) && !IsGenericWrapper(cmdBase))
            chosen = Targets.App(cmdBase);
        if (chosen == null && !string.IsNullOrWhiteSpace(wmClass) && !IsGenericWrapper(wmClass))
            chosen = Targets.App(wmClass);
        if (chosen == null && !string.IsNullOrWhiteSpace(cmdBase))
            chosen = Targets.App(cmdBase);
        if (chosen == null && !string.IsNullOrWhiteSpace(appId))
        {
            int dot = appId.LastIndexOf('.');
            chosen = Targets.App(dot >= 0 && dot + 1 < appId.Length ? appId[(dot + 1)..] : appId);
        }
        if (chosen == null || SharedProcesses.Contains(chosen) || Maintenance.IsMatch(chosen)) return false;
        processName = chosen;
        return true;
    }

    private static bool TryExtractSnap(IEnumerable<string> args, string? wmClass, out string processName, out string? resolvedPath)
    {
        processName = "";
        resolvedPath = null;
        string? snapTarget = null;
        var extraArgs = new List<string>();
        foreach (var arg in args)
        {
            if (snapTarget == null)
            {
                if (arg.StartsWith('-')) continue;
                snapTarget = arg;
            }
            else extraArgs.Add(arg);
        }
        if (HasProtocolOrAppArgument(extraArgs)) return false;
        string? cmd = snapTarget?.Contains('.') == true ? snapTarget.Split('.').LastOrDefault() : snapTarget;
        string? chosen = Targets.App(cmd) ?? (!string.IsNullOrWhiteSpace(wmClass) ? Targets.App(wmClass) : null);
        if (chosen == null || SharedProcesses.Contains(chosen) || Maintenance.IsMatch(chosen)) return false;
        processName = chosen;
        return true;
    }

    private static bool IsGenericWrapper(string name) =>
        SharedProcesses.Contains(name)
        || name.EndsWith("-run", StringComparison.OrdinalIgnoreCase)
        || name.EndsWith(".sh", StringComparison.OrdinalIgnoreCase)
        || name.EndsWith("-wrapper", StringComparison.OrdinalIgnoreCase)
        || name.Equals("wrapper", StringComparison.OrdinalIgnoreCase)
        || name.Equals("start", StringComparison.OrdinalIgnoreCase);

    private static bool HasProtocolOrAppArgument(IEnumerable<string> args)
    {
        foreach (var arg in args)
        {
            if (UriArgument.IsMatch(arg)) return true;
            if (arg.StartsWith("--app=", StringComparison.OrdinalIgnoreCase)
                || arg.StartsWith("--app-id=", StringComparison.OrdinalIgnoreCase)) return true;
        }
        return false;
    }

    private static List<string> TokenizeExec(string exec)
    {
        var tokens = new List<string>();
        var current = new StringBuilder();
        bool inQuotes = false;
        bool hasToken = false;

        for (int i = 0; i < exec.Length; i++)
        {
            char c = exec[i];
            if (inQuotes)
            {
                if (c == '\\' && i + 1 < exec.Length && exec[i + 1] is '"' or '\\' or '$' or '`')
                {
                    current.Append(exec[++i]);
                    hasToken = true;
                }
                else if (c == '"')
                {
                    inQuotes = false;
                }
                else
                {
                    current.Append(c);
                    hasToken = true;
                }
            }
            else
            {
                if (char.IsWhiteSpace(c))
                {
                    if (hasToken)
                    {
                        AddToken(tokens, current.ToString());
                        current.Clear();
                        hasToken = false;
                    }
                }
                else if (c == '"')
                {
                    inQuotes = true;
                    hasToken = true;
                }
                else if (c == '\\' && i + 1 < exec.Length)
                {
                    current.Append(exec[++i]);
                    hasToken = true;
                }
                else
                {
                    current.Append(c);
                    hasToken = true;
                }
            }
        }
        if (hasToken) AddToken(tokens, current.ToString());
        return tokens;
    }

    private static void AddToken(List<string> tokens, string raw)
    {
        var sb = new StringBuilder(raw.Length);
        for (int i = 0; i < raw.Length; i++)
        {
            if (raw[i] == '%' && i + 1 < raw.Length)
            {
                char code = raw[i + 1];
                if (code == '%')
                {
                    sb.Append('%');
                    i++;
                    continue;
                }
                if ("fFuUickdDnNvm".Contains(code))
                {
                    i++;
                    continue;
                }
            }
            sb.Append(raw[i]);
        }
        if (sb.Length > 0) tokens.Add(sb.ToString());
    }

    private static string UnescapeDesktopValue(string value)
    {
        if (!value.Contains('\\')) return value;
        var sb = new StringBuilder(value.Length);
        for (int i = 0; i < value.Length; i++)
        {
            if (value[i] == '\\' && i + 1 < value.Length)
            {
                char next = value[i + 1];
                sb.Append(next switch
                {
                    's' => ' ',
                    'n' => '\n',
                    't' => '\t',
                    'r' => '\r',
                    '\\' => '\\',
                    _ => next
                });
                if (next is not ('s' or 'n' or 't' or 'r' or '\\')) sb.Insert(sb.Length - 1, '\\');
                i++;
            }
            else sb.Append(value[i]);
        }
        return sb.ToString();
    }

    private static bool IsTrue(Dictionary<string, string> fields, string key) =>
        fields.TryGetValue(key, out var val) && val.Trim().Equals("true", StringComparison.OrdinalIgnoreCase);

    private static string? FirstNonEmpty(Dictionary<string, string> fields, params string[] keys)
    {
        foreach (var key in keys)
            if (fields.TryGetValue(key, out var val) && !string.IsNullOrWhiteSpace(val)) return val;
        return null;
    }

    private static bool IsEnvAssignment(string arg)
    {
        int eq = arg.IndexOf('=');
        return eq > 0 && !arg.StartsWith('-') && arg[..eq].All(c => char.IsLetterOrDigit(c) || c == '_');
    }

    private static bool IsHexMask(string arg) =>
        arg.Length > 0 && arg.All(c => Uri.IsHexDigit(c) || c is 'x' or 'X');

    private static string ResolveRealPath(string path)
    {
        try
        {
            var info = new DirectoryInfo(path);
            return info.ResolveLinkTarget(true)?.FullName ?? Path.GetFullPath(path);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or SecurityException)
        {
            return Path.GetFullPath(path);
        }
    }

    private static bool CommandExistsInPath(string command)
    {
        string pathEnv = Environment.GetEnvironmentVariable("PATH") ?? "/usr/local/bin:/usr/bin:/bin";
        foreach (var dir in pathEnv.Split(Path.PathSeparator, StringSplitOptions.RemoveEmptyEntries))
        {
            try
            {
                string full = Path.Combine(dir, command);
                if (File.Exists(full)) return true;
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or ArgumentException) { }
        }
        return false;
    }
}
