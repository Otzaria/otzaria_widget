using System.Text.Json;
using OtzariaWidget.Core.Contracts;
using OtzariaWidget.Core.Preferences;

namespace OtzariaWidget.Core.Paths;

/// <summary>
/// Resolves Otzaria's file locations (storage-and-links.md §1). Read-only, never throws.
/// </summary>
public sealed class OtzariaPathResolver : IOtzariaPathResolver
{
    public const string InnoUninstallKey = @"Software\Microsoft\Windows\CurrentVersion\Uninstall\{EEC4F712-CD05-4D15-A753-509E840A51A5}_is1";
    public const string ProtocolCommandKey = @"Software\Classes\otzaria\shell\open\command";

    private readonly IResolverEnvironment _env;
    private readonly Func<string, OtzariaPreferences?> _prefsReader;

    public OtzariaPathResolver() : this(SystemResolverEnvironment.Instance, null) { }

    /// <param name="env">Environment abstraction (fakes in tests).</param>
    /// <param name="prefsReader">Reads the Hive preferences; may throw / return null (treated as "no prefs"). Default: <see cref="PreferencesReader"/>.</param>
    public OtzariaPathResolver(IResolverEnvironment env, Func<string, OtzariaPreferences?>? prefsReader = null)
    {
        _env = env;
        _prefsReader = prefsReader ?? (p => new PreferencesReader().Read(p));
    }

    public OtzariaPaths Resolve(WidgetAppSettings settings)
    {
        var notes = new List<string>();
        try
        {
            return ResolveCore(settings ?? new WidgetAppSettings(), notes);
        }
        catch (Exception ex)
        {
            notes.Add($"שגיאה בלתי צפויה באיתור הנתיבים: {ex.GetType().Name}");
            return new OtzariaPaths { Diagnostics = notes };
        }
    }

    // ------------------------------------------------------------------------------------------
    private sealed record Capabilities(string? ExePath, bool Portable, string? LibraryDbPath);

    private OtzariaPaths ResolveCore(WidgetAppSettings settings, List<string> notes)
    {
        var appData = Clean(_env.GetEnvironmentVariable("APPDATA"));
        var defaultRoot = appData is null ? null : Combine(appData, "otzaria");

        // ---- 1. dataRoot ------------------------------------------------------------------
        string? dataRoot = null;
        bool portable = false;
        string? portableExe = null; // exe next to portable.marker

        var overrideRoot = Clean(settings.DataRootOverride);
        if (overrideRoot is not null)
        {
            if (_env.DirectoryExists(overrideRoot))
            {
                dataRoot = overrideRoot;
                notes.Add($"תיקיית הנתונים נקבעה ידנית בהגדרות: {overrideRoot}");
            }
            else notes.Add($"תיקיית הנתונים שהוגדרה ידנית אינה קיימת: {overrideRoot}");
        }

        if (dataRoot is null && defaultRoot is not null && LooksLikeDataRoot(defaultRoot))
        {
            dataRoot = defaultRoot;
            notes.Add($"תיקיית הנתונים: {defaultRoot} (%APPDATA%\\otzaria)");
        }

        // exe candidates are needed for portable discovery and for the exe resolution later
        var capsFromDefault = defaultRoot is null ? null : ReadCapabilities(Combine(defaultRoot, "otzaria_capabilities.json"));
        if (dataRoot is null)
        {
            var exeCandidates = new List<(string Exe, string Source)>();
            AddIfNotNull(exeCandidates, Clean(settings.OtzariaExeOverride), "הגדרות הווידג'ט");
            AddIfNotNull(exeCandidates, capsFromDefault?.ExePath, "otzaria_capabilities.json");
            foreach (var (e, s) in RegistryExeCandidates()) exeCandidates.Add((e, s));
            foreach (var (e, s) in KnownLocationExeCandidates()) exeCandidates.Add((e, s));

            foreach (var (candExe, source) in exeCandidates)
            {
                var exeDir = DirName(candExe);
                if (exeDir is null) continue;
                if (_env.FileExists(Combine(exeDir, "portable.marker")))
                {
                    var candidate = Combine(exeDir, "otzaria_data");
                    if (_env.DirectoryExists(candidate))
                    {
                        dataRoot = candidate;
                        portable = true;
                        portableExe = candExe;
                        notes.Add($"מצב נייד: תיקיית הנתונים {candidate} (נמצא portable.marker ליד {candExe}, מקור: {source})");
                        break;
                    }
                }
            }
        }

        if (dataRoot is null && defaultRoot is not null && _env.DirectoryExists(defaultRoot))
        {
            dataRoot = defaultRoot;
            notes.Add($"תיקיית הנתונים: {defaultRoot} (קיימת אך ריקה מקובצי אוצריא מוכרים)");
        }

        if (dataRoot is null)
            notes.Add("לא נמצאה תיקיית נתונים של אוצריא (%APPDATA%\\otzaria או תיקיית otzaria_data של גרסה ניידת)");

        // ---- 2. capabilities & library_*.txt (in dataRoot) --------------------------------
        Capabilities? caps = null;
        string? libraryPathTxt = null, libraryDbDirTxt = null;
        if (dataRoot is not null)
        {
            caps = ReadCapabilities(Combine(dataRoot, "otzaria_capabilities.json"));
            libraryPathTxt = ReadPathFile(Combine(dataRoot, "library_path.txt"));
            libraryDbDirTxt = ReadPathFile(Combine(dataRoot, "library_database_path.txt"));
            if (!portable && caps?.Portable == true) portable = true;
        }
        // capabilities of ANOTHER data root (e.g. a stale %APPDATA% file while we use a portable root) must not leak in
        if (dataRoot is null || SamePath(dataRoot, defaultRoot)) caps ??= capsFromDefault;

        // ---- 3. preferences ----------------------------------------------------------------
        string? hive = null;
        OtzariaPreferences? prefs = null;
        if (dataRoot is not null)
        {
            var hivePath = Combine(dataRoot, "app_preferences.hive");
            if (_env.FileExists(hivePath))
            {
                hive = hivePath;
                try { prefs = _prefsReader(hivePath); }
                catch { prefs = null; }
                if (prefs is null)
                    notes.Add("לא ניתן היה לקרוא את app_preferences.hive; משתמש בקובצי הטקסט ובברירות המחדל");
            }
            else notes.Add("לא נמצא app_preferences.hive בתיקיית הנתונים");
        }

        // ---- 4. libraryPath ----------------------------------------------------------------
        var prefLibraryPath = Clean(prefs?.LibraryPath);
        string? libraryPath = prefLibraryPath;
        if (libraryPath is not null) notes.Add("נתיב הספרייה מתוך ההגדרות של אוצריא (key-library-path)");
        else if (libraryPathTxt is not null)
        {
            libraryPath = libraryPathTxt;
            notes.Add("נתיב הספרייה מתוך library_path.txt");
        }
        else if (dataRoot is not null)
        {
            libraryPath = Combine(dataRoot, "books");
            notes.Add("נתיב הספרייה: ברירת מחדל (<תיקיית נתונים>\\books)");
        }

        // ---- 5. databases directory per file ----------------------------------------------
        var dbDirs = new List<(string Dir, string Source)>();
        AddIfNotNull(dbDirs, Clean(settings.DatabasesDirOverride), "הגדרות הווידג'ט");
        AddIfNotNull(dbDirs, Clean(prefs?.DatabasesPath), "key-databases-path");
        if (dataRoot is not null) dbDirs.Add((Combine(dataRoot, "databases"), "<תיקיית נתונים>\\databases"));
        if (libraryPath is not null)
        {
            var parent = DirName(libraryPath);
            if (parent is not null) dbDirs.Add((Combine(parent, "databases"), "<תיקיית הספרייה>\\..\\databases"));
        }

        var userState = FindInDirs("user_state.db", dbDirs, notes);
        var personalNotes = FindInDirs("personal_notes.db", dbDirs, notes);

        // ---- 6. seforim.db -----------------------------------------------------------------
        var libraryDb = ResolveLibraryDb(settings, caps, libraryDbDirTxt, prefs, libraryPathTxt, dataRoot, notes);

        // ---- 7. otzaria.exe ----------------------------------------------------------------
        var exe = ResolveExe(settings, caps, portable, portableExe, dataRoot, notes);

        return new OtzariaPaths
        {
            DataRoot = dataRoot,
            UserStateDb = userState,
            PersonalNotesDb = personalNotes,
            PreferencesHive = hive,
            LibraryDb = libraryDb,
            OtzariaExe = exe,
            IsPortable = portable,
            Diagnostics = notes,
        };
    }

    // ------------------------------------------------------------------------------------------
    private string? FindInDirs(string fileName, List<(string Dir, string Source)> dirs, List<string> notes)
    {
        foreach (var (dir, source) in dirs)
        {
            var p = Combine(dir, fileName);
            if (_env.FileExists(p))
            {
                notes.Add($"{fileName}: {p} (מקור: {source})");
                return p;
            }
        }
        notes.Add($"לא נמצא קובץ {fileName}");
        return null;
    }

    private string? ResolveLibraryDb(WidgetAppSettings settings, Capabilities? caps, string? libraryDbDirTxt,
        OtzariaPreferences? prefs, string? libraryPathTxt, string? dataRoot, List<string> notes)
    {
        var candidates = new List<(string Path, string Source)>();

        var ov = Clean(settings.LibraryDbOverride);
        if (ov is not null)
        {
            // accept either the full path of seforim.db or its folder
            if (_env.DirectoryExists(ov)) candidates.Add((Combine(ov, "seforim.db"), "הגדרות הווידג'ט (תיקייה)"));
            else candidates.Add((ov, "הגדרות הווידג'ט"));
        }
        AddIfNotNull(candidates, Clean(caps?.LibraryDbPath), "otzaria_capabilities.json (libraryDbPath)");
        if (libraryDbDirTxt is not null) candidates.Add((Combine(libraryDbDirTxt, "seforim.db"), "library_database_path.txt"));
        var libPath = Clean(prefs?.LibraryPath);
        if (libPath is not null)
        {
            var folder = Clean(prefs?.LibraryFolderName);
            var p = folder is null ? Combine(libPath, "seforim.db") : Combine(Combine(libPath, folder), "seforim.db");
            candidates.Add((p, "key-library-path"));
        }
        if (libraryPathTxt is not null) candidates.Add((Combine(libraryPathTxt, "seforim.db"), "library_path.txt"));
        if (dataRoot is not null) candidates.Add((Combine(Combine(dataRoot, "books"), "seforim.db"), "ברירת מחדל (<תיקיית נתונים>\\books)"));
        var programData = Clean(_env.GetEnvironmentVariable("ProgramData"));
        if (programData is not null) candidates.Add((Combine(Combine(Combine(programData, "otzaria"), "books"), "seforim.db"), "ברירת מחדל (ProgramData)"));

        foreach (var (path, source) in candidates)
        {
            if (_env.FileExists(path))
            {
                notes.Add($"seforim.db: {path} (מקור: {source})");
                return path;
            }
        }
        notes.Add("לא נמצא קובץ seforim.db");
        return null;
    }

    private string? ResolveExe(WidgetAppSettings settings, Capabilities? caps, bool portable, string? portableExe,
        string? dataRoot, List<string> notes)
    {
        var ov = Clean(settings.OtzariaExeOverride);
        if (ov is not null)
        {
            if (_env.FileExists(ov)) { notes.Add($"otzaria.exe: {ov} (נקבע ידנית בהגדרות)"); return ov; }
            if (_env.DirectoryExists(ov) && _env.FileExists(Combine(ov, "otzaria.exe")))
            {
                var p = Combine(ov, "otzaria.exe");
                notes.Add($"otzaria.exe: {p} (תיקייה שנקבעה ידנית בהגדרות)");
                return p;
            }
            notes.Add($"הקובץ שהוגדר ידנית עבור otzaria.exe אינו קיים: {ov}");
        }

        if (caps?.ExePath is { } cp && LooksLikeOtzariaExe(cp) && _env.FileExists(cp))
        {
            notes.Add($"otzaria.exe: {cp} (otzaria_capabilities.json)");
            return cp;
        }
        if (portable && portableExe is not null && _env.FileExists(portableExe))
        {
            notes.Add($"otzaria.exe: {portableExe} (גרסה ניידת)");
            return portableExe;
        }
        if (portable && dataRoot is not null)
        {
            var parent = DirName(dataRoot);
            if (parent is not null)
            {
                var p = Combine(parent, "otzaria.exe");
                if (_env.FileExists(p)) { notes.Add($"otzaria.exe: {p} (ליד otzaria_data)"); return p; }
            }
        }

        foreach (var (exe, source) in RegistryExeCandidates())
        {
            if (LooksLikeOtzariaExe(exe) && _env.FileExists(exe)) { notes.Add($"otzaria.exe: {exe} (מקור: {source})"); return exe; }
        }
        foreach (var (exe, source) in KnownLocationExeCandidates())
        {
            if (_env.FileExists(exe)) { notes.Add($"otzaria.exe: {exe} (מקור: {source})"); return exe; }
        }

        notes.Add("לא נמצא otzaria.exe");
        return null;
    }

    /// <summary>Exe paths from HKCU/HKLM protocol command and the Inno uninstall key (not checked for existence).</summary>
    private IEnumerable<(string Exe, string Source)> RegistryExeCandidates()
    {
        foreach (var root in new[] { RegistryRoot.CurrentUser, RegistryRoot.LocalMachine })
        {
            var rootName = root == RegistryRoot.CurrentUser ? "HKCU" : "HKLM";
            string? cmd = null;
            try { cmd = _env.GetRegistryString(root, ProtocolCommandKey, null); } catch { }
            var exe = ExpandVars(ParseCommandExe(cmd));
            if (exe is not null) yield return (exe, $"{rootName}\\...\\otzaria\\shell\\open\\command");
        }
        foreach (var root in new[] { RegistryRoot.CurrentUser, RegistryRoot.LocalMachine })
        {
            var rootName = root == RegistryRoot.CurrentUser ? "HKCU" : "HKLM";
            string? loc = null, icon = null;
            try
            {
                loc = _env.GetRegistryString(root, InnoUninstallKey, "InstallLocation");
                icon = _env.GetRegistryString(root, InnoUninstallKey, "DisplayIcon");
            }
            catch { }
            loc = ExpandVars(Clean(loc)?.Trim('"'));
            if (loc is not null) yield return (Combine(loc, "otzaria.exe"), $"{rootName} Uninstall\\InstallLocation");
            var iconExe = ExpandVars(ParseCommandExe(icon));
            if (iconExe is not null) yield return (iconExe, $"{rootName} Uninstall\\DisplayIcon");
        }
    }

    private IEnumerable<(string Exe, string Source)> KnownLocationExeCandidates()
    {
        var pf = Clean(_env.GetEnvironmentVariable("ProgramFiles"));
        var pf6432 = Clean(_env.GetEnvironmentVariable("ProgramW6432"));
        var local = Clean(_env.GetEnvironmentVariable("LOCALAPPDATA"));
        if (local is not null) yield return (Combine(Combine(Combine(local, "Programs"), "Otzaria"), "otzaria.exe"), "תיקיית התקנה סטנדרטית (למשתמש)");
        if (pf6432 is not null) yield return (Combine(Combine(pf6432, "Otzaria"), "otzaria.exe"), "תיקיית התקנה סטנדרטית (Program Files)");
        if (pf is not null) yield return (Combine(Combine(pf, "Otzaria"), "otzaria.exe"), "תיקיית התקנה סטנדרטית (Program Files)");
    }

    /// <summary>Extracts the exe path from a command like <c>"C:\x\otzaria.exe" "%1"</c> or <c>C:\x\otzaria.exe,0</c>.</summary>
    public static string? ParseCommandExe(string? command)
    {
        if (string.IsNullOrWhiteSpace(command)) return null;
        var s = command.Trim();
        string token;
        if (s.StartsWith('"'))
        {
            var end = s.IndexOf('"', 1);
            if (end <= 1) return null;
            token = s[1..end];
        }
        else
        {
            var idx = s.IndexOf(".exe", StringComparison.OrdinalIgnoreCase);
            if (idx < 0) return null;
            token = s[..(idx + 4)];
        }
        token = token.Trim();
        // "path\otzaria.exe,0" (icon reference)
        var comma = token.LastIndexOf(',');
        if (comma > 0 && token[..comma].EndsWith(".exe", StringComparison.OrdinalIgnoreCase)) token = token[..comma];
        return token.EndsWith(".exe", StringComparison.OrdinalIgnoreCase) ? token : null;
    }

    /// <summary>
    /// Exe paths that come from data files / the registry (not from the user's own widget setting) must at least be named
    /// otzaria*.exe, so a tampered capabilities file cannot make the widget start an arbitrary program.
    /// </summary>
    private static bool LooksLikeOtzariaExe(string path)
    {
        try
        {
            var name = Path.GetFileName(path);
            return name.StartsWith("otzaria", StringComparison.OrdinalIgnoreCase) && name.EndsWith(".exe", StringComparison.OrdinalIgnoreCase);
        }
        catch { return false; }
    }

    private static bool SamePath(string? a, string? b)
    {
        if (a is null || b is null) return false;
        return string.Equals(a.TrimEnd('\\', '/'), b.TrimEnd('\\', '/'), StringComparison.OrdinalIgnoreCase);
    }

    /// <summary>Expands %NAME% via the environment abstraction (REG_SZ commands may contain them; REG_EXPAND_SZ is already expanded by the registry API).</summary>
    private string? ExpandVars(string? s)
    {
        if (s is null || s.IndexOf('%') < 0) return s;
        return System.Text.RegularExpressions.Regex.Replace(s, @"%([^%\r\n]+)%", m =>
            Clean(_env.GetEnvironmentVariable(m.Groups[1].Value)) ?? m.Value);
    }

    private bool LooksLikeDataRoot(string dir) =>
        _env.DirectoryExists(dir)
        && (_env.FileExists(Combine(dir, "app_preferences.hive")) || _env.DirectoryExists(Combine(dir, "databases")));

    private string? ReadPathFile(string file)
    {
        var text = _env.ReadAllText(file);
        if (text is null) return null;
        var s = text.TrimStart('\uFEFF').Trim().Trim('"');
        return s.Length == 0 ? null : s;
    }

    private Capabilities? ReadCapabilities(string file)
    {
        var text = _env.ReadAllText(file);
        if (string.IsNullOrWhiteSpace(text)) return null;
        try
        {
            using var doc = JsonDocument.Parse(text.TrimStart('\uFEFF'));
            if (doc.RootElement.ValueKind != JsonValueKind.Object) return null;
            var r = doc.RootElement;
            return new Capabilities(
                Str(r, "exePath"),
                r.TryGetProperty("portable", out var p) && p.ValueKind == JsonValueKind.True,
                Str(r, "libraryDbPath"));
        }
        catch { return null; }
    }

    private static string? Str(JsonElement obj, string name) =>
        obj.TryGetProperty(name, out var v) && v.ValueKind == JsonValueKind.String ? Clean(v.GetString()) : null;

    private static void AddIfNotNull(List<(string, string)> list, string? value, string source)
    {
        if (value is not null) list.Add((value, source));
    }

    private static string? Clean(string? s)
    {
        if (string.IsNullOrWhiteSpace(s)) return null;
        return s.Trim();
    }

    private static string Combine(string a, string b)
    {
        try { return Path.Combine(a, b); } catch { return a.TrimEnd('\\', '/') + "\\" + b; }
    }

    private static string? DirName(string path)
    {
        try { return Path.GetDirectoryName(path.TrimEnd('\\', '/')); } catch { return null; }
    }
}
