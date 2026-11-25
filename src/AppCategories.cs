namespace Pulso;

internal static class AppCategories
{
    private static readonly Dictionary<string, (string Label, int Order)> Categories = new(StringComparer.OrdinalIgnoreCase);
    private static readonly Dictionary<string, string> Processes = new(StringComparer.OrdinalIgnoreCase);
    private static readonly Dictionary<string, string> LinuxAliases = new(StringComparer.OrdinalIgnoreCase)
    {
        ["google-chrome"] = "browsers", ["google-chrome-stable"] = "browsers", ["google-chrome-beta"] = "browsers",
        ["google-chrome-unstable"] = "browsers", ["chromium-browser"] = "browsers", ["firefox-esr"] = "browsers",
        ["firefox-bin"] = "browsers", ["microsoft-edge"] = "browsers", ["microsoft-edge-stable"] = "browsers",
        ["microsoft-edge-beta"] = "browsers", ["microsoft-edge-dev"] = "browsers", ["brave-browser"] = "browsers",
        ["vivaldi-stable"] = "browsers", ["zen-browser"] = "browsers", ["epiphany"] = "browsers",
        ["falkon"] = "browsers", ["midori"] = "browsers", ["torbrowser-launcher"] = "browsers",
        ["telegram-desktop"] = "communication", ["signal-desktop"] = "communication", ["element-desktop"] = "communication",
        ["whatsapp-for-linux"] = "communication", ["teams-for-linux"] = "communication", ["vesktop"] = "communication",
        ["ferdium"] = "communication", ["evolution"] = "communication", ["geary"] = "communication",
        ["kmail"] = "communication", ["pidgin"] = "communication", ["hexchat"] = "communication",
        ["fractal"] = "communication", ["discord-canary"] = "communication", ["discord-ptb"] = "communication",
        ["antigravity"] = "development", ["code-oss"] = "development", ["codium"] = "development",
        ["kate"] = "development", ["kdevelop"] = "development", ["qtcreator"] = "development",
        ["geany"] = "development", ["neovide"] = "development", ["zed"] = "development",
        ["android-studio"] = "development", ["pycharm"] = "development", ["idea"] = "development",
        ["webstorm"] = "development", ["clion"] = "development", ["rider"] = "development",
        ["datagrip"] = "development", ["goland"] = "development", ["phpstorm"] = "development",
        ["rubymine"] = "development", ["dbeaver-ce"] = "development", ["gitkraken"] = "development",
        ["meld"] = "development", ["sublime_merge"] = "development",
        ["lutris"] = "games", ["heroic"] = "games", ["prismlauncher"] = "games", ["bottles"] = "games",
        ["itch"] = "games", ["minigalaxy"] = "games", ["steam-runtime"] = "games", ["rpcs3"] = "games",
        ["ryujinx"] = "games", ["yuzu"] = "games", ["ppsspp"] = "games", ["duckstation"] = "games",
        ["obs"] = "video", ["kdenlive"] = "video", ["totem"] = "video", ["celluloid"] = "video",
        ["haruna"] = "video", ["smplayer"] = "video", ["shotcut"] = "video", ["openshot-qt"] = "video",
        ["pitivi"] = "video", ["stremio"] = "video", ["freetube"] = "video", ["kodi"] = "video", ["dragon"] = "video",
        ["rhythmbox"] = "audio", ["elisa"] = "audio", ["clementine"] = "audio", ["strawberry"] = "audio",
        ["lollypop"] = "audio", ["ardour"] = "audio", ["lmms"] = "audio", ["musescore"] = "audio",
        ["mscore"] = "audio", ["amberol"] = "audio", ["deadbeef"] = "audio", ["easytag"] = "audio", ["qjackctl"] = "audio",
        ["darktable"] = "photos", ["rawtherapee"] = "photos", ["digikam"] = "photos", ["eog"] = "photos",
        ["gwenview"] = "photos", ["loupe"] = "photos", ["kolourpaint"] = "photos", ["pinta"] = "photos",
        ["shotwell"] = "photos", ["scribus"] = "photos", ["aseprite"] = "photos", ["mypaint"] = "photos",
        ["evince"] = "reading", ["okular"] = "reading", ["foliate"] = "reading", ["zathura"] = "reading",
        ["akregator"] = "reading", ["newsflash"] = "reading", ["mupdf"] = "reading", ["xreader"] = "reading",
        ["atril"] = "reading", ["qpdfview"] = "reading",
        ["gnome-calculator"] = "tools", ["kcalc"] = "tools", ["gnome-clocks"] = "tools",
        ["file-roller"] = "tools", ["ark"] = "tools", ["flameshot"] = "tools", ["spectacle"] = "tools",
        ["gnome-screenshot"] = "tools", ["baobab"] = "tools", ["filelight"] = "tools", ["gparted"] = "tools",
        ["timeshift"] = "tools", ["bleachbit"] = "tools", ["synaptic"] = "tools", ["gnome-tweaks"] = "tools",
        ["missioncenter"] = "tools",
        ["gnome-maps"] = "maps", ["marble"] = "maps", ["qgis"] = "maps",
        ["libreoffice"] = "work", ["lowriter"] = "work", ["localc"] = "work", ["loimpress"] = "work",
        ["lodraw"] = "work", ["xournalpp"] = "work", ["logseq"] = "work", ["joplin-desktop"] = "work",
        ["anki"] = "work", ["korganizer"] = "work", ["gnome-calendar"] = "work", ["planner"] = "work",
        ["abiword"] = "work", ["gnumeric"] = "work",
        ["kmymoney"] = "shopping", ["homebank"] = "shopping", ["skrooge"] = "shopping"
    };
    private static readonly (string CategoryId, string[] DesktopTokens)[] DesktopCategoryRules =
    [
        ("games", ["Game"]),
        ("browsers", ["WebBrowser"]),
        ("communication", ["InstantMessaging", "Chat", "Email", "Telephony", "VideoConference", "IRCClient"]),
        ("development", ["IDE", "Development", "RevisionControl", "Debugger", "WebDevelopment", "Building", "GUIDesigner"]),
        ("video", ["Video", "TV", "AudioVideoEditing"]),
        ("audio", ["Audio", "Music", "Mixer", "Sequencer", "Tuner", "Midi"]),
        ("photos", ["Graphics", "Photography", "2DGraphics", "3DGraphics", "RasterGraphics", "VectorGraphics", "ImageProcessing"]),
        ("shopping", ["Finance", "Economy"]),
        ("reading", ["News", "Feed", "Literature", "Publishing"]),
        ("maps", ["Maps", "Geography", "Navigation"]),
        ("health", ["Sports", "MedicalSoftware"]),
        ("work", ["Office", "WordProcessor", "Spreadsheet", "Presentation", "Calendar", "ProjectManagement", "Education", "Science", "Math", "Dictionary", "TextEditor"]),
        ("video", ["AudioVideo"]),
        ("tools", ["Utility", "Calculator", "Clock", "Archiving", "Compression", "FileTools", "Security", "Accessibility", "System", "Settings"])
    ];

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

    internal static string ForLinux(string processName, string? displayName = null, string? path = null,
        IEnumerable<string>? desktopCategories = null)
    {
        string process = processName.Trim();
        if (process.EndsWith(".exe", StringComparison.OrdinalIgnoreCase)) process = process[..^4];
        if (Processes.TryGetValue(process, out var category) || LinuxAliases.TryGetValue(process, out category)) return category;
        if (displayName != null)
        {
            string title = displayName.Trim();
            if (Processes.TryGetValue(title, out category) || LinuxAliases.TryGetValue(title, out category)) return category;
        }
        if (path != null)
        {
            string unixPath = path.Replace('\\', '/');
            if (unixPath.Contains("/steamapps/common/", StringComparison.OrdinalIgnoreCase) ||
                unixPath.Contains("/GOG Games/", StringComparison.OrdinalIgnoreCase) ||
                unixPath.Contains("/.local/share/Steam/", StringComparison.OrdinalIgnoreCase) ||
                unixPath.Contains("/Lutris/", StringComparison.OrdinalIgnoreCase) ||
                unixPath.Contains("/Heroic/", StringComparison.OrdinalIgnoreCase)) return "games";
        }
        if (desktopCategories != null)
        {
            var tokens = desktopCategories.Select(t => t.Trim()).Where(t => t.Length > 0).ToHashSet(StringComparer.OrdinalIgnoreCase);
            foreach (var (id, ruleTokens) in DesktopCategoryRules)
                if (ruleTokens.Any(tokens.Contains)) return id;
        }
        return "other";
    }

    private static (string Label, int Order) Category(string? id) =>
        id != null && Categories.TryGetValue(id.Trim(), out var category) ? category : Categories["other"];

    internal static string Label(string id) => Category(id).Label;
    internal static int Order(string id) => Category(id).Order;
}
