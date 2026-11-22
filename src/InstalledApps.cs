using System.Diagnostics;
using System.Reflection.PortableExecutable;
using System.Runtime.InteropServices;
using System.Security;
using System.Text.RegularExpressions;
using System.Xml;
using System.Xml.Linq;
using Microsoft.Win32;

namespace Pulso;

internal sealed record DiscoveredApp(string ProcessName, string DisplayName, string CategoryId);
internal sealed record AppScan(IReadOnlyList<DiscoveredApp> Apps, bool Partial);

internal static class InstalledApps
{
    internal static AppScan Scan(CancellationToken token)
    {
        token.ThrowIfCancellationRequested();
        var catalog = new InstalledAppCatalog(token);
        foreach (var folder in new[] { Environment.SpecialFolder.Programs, Environment.SpecialFolder.CommonPrograms })
            catalog.ReadSource(() => catalog.ReadStartMenu(Environment.GetFolderPath(folder, Environment.SpecialFolderOption.DoNotVerify)));
        foreach (var hive in new[] { RegistryHive.CurrentUser, RegistryHive.LocalMachine })
        {
            catalog.ReadAppPaths(hive, RegistryView.Registry32);
            if (Environment.Is64BitOperatingSystem) catalog.ReadAppPaths(hive, RegistryView.Registry64);
        }
        catalog.ReadStoreApplications();
        return catalog.Result();
    }
}

internal sealed partial class InstalledAppCatalog
{
    private readonly CancellationToken token;
    private readonly Dictionary<string, (DiscoveredApp App, int Priority)> apps = new(StringComparer.OrdinalIgnoreCase);
    private readonly Dictionary<string, Dictionary<string, string>> manifests = new(StringComparer.OrdinalIgnoreCase);
    private bool partial;

    internal InstalledAppCatalog(CancellationToken token) => this.token = token;

    // Multiple shortcuts can run the same interpreter, shell or browser proxy with different content.
    private static readonly HashSet<string> SharedProcesses = new(StringComparer.OrdinalIgnoreCase)
    {
        "system", "idle", "svchost", "services", "lsass", "lsaiso", "wininit", "smss", "winlogon", "fontdrvhost",
        "dasHost", "taskhostw", "taskhostex", "runtimebroker", "backgroundtaskhost", "dllhost", "rundll32", "mmc",
        "control", "cmd", "wt", "powershell", "powershell_ise", "pwsh", "wscript", "cscript", "mshta", "wsl", "wslhost",
        "msiexec", "regsvr32", "java", "javaw", "python", "pythonw", "pyw", "node", "electron", "rubyw", "wish",
        "wwahost", "browser_broker", "msedgewebview2", "msedge_proxy", "chrome_proxy", "pwahelper",
        "shellhost", "searchapp", "searchui", "shellexperiencehost", "openwith", "systemsettingsadminflows",
        "securityhealthsystray", "securityhealthhost", "sechealthui", "smartscreen", "logonui", "userinit",
        "regedit", "regedt32", "appinstaller", "werfault", "wermgr", "windowsinternal.composableshell.experiences.textinput.inputapp"
    };
    private static readonly Regex Maintenance = new(
        @"^unins(?:t|\d)|^setup|^(?:install|update|maintenance|repair|vc_?redist)(?:[-_. \d]|$)|" +
        @"(?:setup|installer|updater|autoupdate|crashpad[_-]?handler|crashreporter)(?:[-_. ]?(?:x86|x64|arm64|win32|win64|[\d.]+))?$",
        RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);
    private static readonly Regex ProcessStart = new(
        @"^--processStart(?:=|\s+)(?:""([^""]+\.exe)""|([^\s""]+\.exe))$",
        RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);

    internal AppScan Result()
    {
        token.ThrowIfCancellationRequested();
        var sorted = apps.Values.Select(entry => entry.App).OrderBy(app => AppCategories.Order(app.CategoryId))
            .ThenBy(app => app.DisplayName, StringComparer.OrdinalIgnoreCase).ThenBy(app => app.ProcessName, StringComparer.OrdinalIgnoreCase);
        return new(sorted.ToArray(), partial);
    }

    private static bool ReadFailure(Exception ex) => ex is IOException or UnauthorizedAccessException or SecurityException
        or COMException or ArgumentException or NotSupportedException or XmlException or Microsoft.CSharp.RuntimeBinder.RuntimeBinderException;

    internal void ReadSource(Action read)
    {
        token.ThrowIfCancellationRequested();
        try { read(); }
        catch (Exception ex) when (ReadFailure(ex)) { partial = true; }
    }

    internal void ReadStartMenu(string folder)
    {
        var root = LocalPath(folder);
        if (root == null) { partial = true; return; }
        if (!PlainPath(root)) { partial = true; return; }
        var pending = new Stack<string>();
        pending.Push(root);
        while (pending.TryPop(out var directory))
        {
            ReadSource(() =>
            {
                foreach (var entry in new DirectoryInfo(directory).EnumerateFileSystemInfos())
                {
                    token.ThrowIfCancellationRequested();
                    if (entry.Attributes.HasFlag(FileAttributes.ReparsePoint)) continue;
                    if (entry is DirectoryInfo) pending.Push(entry.FullName);
                    else if (entry.Extension.Equals(".lnk", StringComparison.OrdinalIgnoreCase))
                        ReadSource(() => ReadStartShortcut(entry.FullName));
                }
            });
        }
    }

    internal void ReadAppPaths(RegistryHive hive, RegistryView view) => ReadSource(() =>
    {
        using var registry = RegistryKey.OpenBaseKey(hive, view);
        using var paths = registry.OpenSubKey(@"Software\Microsoft\Windows\CurrentVersion\App Paths");
        if (paths == null) return;
        foreach (var key in paths.GetSubKeyNames())
            ReadSource(() =>
            {
                using var registration = paths.OpenSubKey(key);
                ReadAppPath(registration?.GetValue(null, null, RegistryValueOptions.DoNotExpandEnvironmentNames) as string);
            });
    });

    internal void ReadAppPath(string? path) => AddExecutable(path, null, 1);

    private void ReadStartShortcut(string linkPath)
    {
        var (target, arguments) = ShortcutTarget(linkPath);
        string? path = LocalPath(target);
        if (path == null) return;
        string name = Path.GetFileNameWithoutExtension(linkPath);
        if (arguments.Length == 0) { AddExecutable(path, name, 2); return; }
        // A shortcut with arguments can represent a URL, a document or a maintenance task.
        // Squirrel's processStart is special: it names the separate executable that owns the app window.
        if (!Path.GetFileName(path).Equals("Update.exe", StringComparison.OrdinalIgnoreCase)) return;
        var match = ProcessStart.Match(arguments);
        if (!match.Success || !PlainPath(path)) return;
        string executable = match.Groups[1].Success ? match.Groups[1].Value : match.Groups[2].Value;
        if (executable != Path.GetFileName(executable) || Targets.App(executable) == null) return;
        string folder = Path.GetDirectoryName(path)!;
        if (AddExecutable(Path.Combine(folder, executable), name, 2)) return;
        var installation = new DirectoryInfo(folder);
        if (installation.Parent == null) return;
        var versions = installation.EnumerateDirectories("app-*").Where(d => !d.Attributes.HasFlag(FileAttributes.ReparsePoint))
            .Select(d =>
            {
                token.ThrowIfCancellationRequested();
                return (Directory: d, Version: Version.TryParse(d.Name[4..].Split('-', 2)[0], out var v) ? v : null);
            })
            .Where(item => item.Version != null).OrderByDescending(item => item.Version)
            .ThenByDescending(item => item.Directory.Name, StringComparer.OrdinalIgnoreCase);
        foreach (var version in versions)
            if (AddExecutable(Path.Combine(version.Directory.FullName, executable), name, 2)) return;
    }

    internal void ReadPackageApplication(string folder, string appId, string? name) => ReadSource(() =>
    {
        string? root = LocalPath(folder);
        if (root == null) { partial = true; return; }
        if (!manifests.TryGetValue(root, out var applications))
        {
            applications = new(StringComparer.OrdinalIgnoreCase);
            manifests.Add(root, applications);
            string manifest = Path.Combine(root, "AppxManifest.xml");
            if (!PlainPath(manifest)) { partial = true; return; }
            using var reader = XmlReader.Create(manifest, new XmlReaderSettings { DtdProcessing = DtdProcessing.Prohibit,
                XmlResolver = null, MaxCharactersInDocument = 8 * 1024 * 1024 });
            var package = XDocument.Load(reader).Root;
            if (package?.Name.LocalName != "Package") { partial = true; return; }
            var ns = package.Name.Namespace;
            foreach (var app in package.Element(ns + "Applications")?.Elements(ns + "Application") ?? [])
                if (app.Attribute("Id")?.Value is { } id && app.Attribute("Executable")?.Value is { } executable)
                    applications.TryAdd(id, executable);
        }
        if (!applications.TryGetValue(appId, out var relative) || Path.IsPathRooted(relative)
            || relative.Split('\\', '/').Contains("..") || relative.Contains(':') || relative.Contains('%')) return;
        AddExecutable(Path.Combine(root, relative), name, 3);
    });

    private bool AddExecutable(string? candidate, string? preferredName, int priority)
    {
        token.ThrowIfCancellationRequested();
        try
        {
            string? path = LocalPath(candidate);
            if (path == null || !Path.GetExtension(path).Equals(".exe", StringComparison.OrdinalIgnoreCase)) return false;
            if (Targets.App(Path.GetFileName(path)) is not { } process || SharedProcesses.Contains(process) || Maintenance.IsMatch(process)
                || !PlainPath(path)) return false;
            using var stream = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete);
            using var pe = new PEReader(stream);
            if (pe.PEHeaders.PEHeader?.Subsystem != Subsystem.WindowsGui ||
                pe.PEHeaders.CoffHeader.Characteristics.HasFlag(Characteristics.Dll)) return false;

            if (apps.TryGetValue(process, out var current) && current.Priority > priority) return true;
            string? label = preferredName ?? FileVersionInfo.GetVersionInfo(path).FileDescription;
            string name = string.IsNullOrWhiteSpace(label) || label.Length > 200 || label.Any(char.IsControl) ? process : label.Trim();
            if (current.App == null || current.Priority < priority || StringComparer.Ordinal.Compare(name, current.App.DisplayName) < 0)
                apps[process] = (new(process, name, AppCategories.ForWindows(process, name, path)), priority);
            return true;
        }
        catch (Exception ex) when (ReadFailure(ex) || ex is BadImageFormatException or System.ComponentModel.Win32Exception) { return false; }
    }

    // Validate the drive before looking up any attributes, including those of redirected Start menus.
    private static string? LocalPath(string? input)
    {
        if (string.IsNullOrWhiteSpace(input)) return null;
        string value = Environment.ExpandEnvironmentVariables(input.Trim());
        if (value.Length > 1 && value[0] == '"' && value[^1] == '"') value = value[1..^1];
        if (value.Length < 3 || !char.IsAsciiLetter(value[0]) || value[1] != ':' || value[2] is not ('\\' or '/')
            || value.AsSpan(2).Contains(':') || value.Contains('"') || value.IndexOfAny(Path.GetInvalidPathChars()) >= 0) return null;
        string path = Path.GetFullPath(value);
        return new DriveInfo(Path.GetPathRoot(path)!).DriveType is DriveType.Fixed or DriveType.Removable or DriveType.Ram ? path : null;
    }

    // A local drive can contain junctions or symbolic links to other volumes and network shares.
    private static bool PlainPath(string path)
    {
        string current = Path.GetPathRoot(path)!;
        try
        {
            foreach (string part in path[current.Length..].Split(Path.DirectorySeparatorChar, StringSplitOptions.RemoveEmptyEntries))
            {
                current = Path.Combine(current, part);
                if (File.GetAttributes(current).HasFlag(FileAttributes.ReparsePoint)) return false;
            }
            return true;
        }
        catch (Exception ex) when (ex is FileNotFoundException or DirectoryNotFoundException) { return false; }
    }
}
