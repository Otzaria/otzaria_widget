using System.Text.Json;
using System.Text.Json.Serialization;
using OtzariaWidget.Core.Contracts;

namespace OtzariaWidget.Core.Settings;

/// <summary>
/// JSON persistence of <see cref="WidgetAppSettings"/> (default: %LOCALAPPDATA%\OtzariaWidget\settings.json).
/// Atomic writes, tolerant loading (corrupt file => defaults + settings.json.bak), debounced change notification.
/// </summary>
public sealed class JsonSettingsStore : ISettingsStore, IDisposable
{
    public const string FileName = "settings.json";

    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        WriteIndented = true,
        PropertyNameCaseInsensitive = true,
        Converters = { new JsonStringEnumConverter() },
        ReadCommentHandling = JsonCommentHandling.Skip,
        AllowTrailingCommas = true,
    };

    private readonly string _directory;
    private readonly string _filePath;
    private readonly TimeSpan _debounce;
    private readonly object _gate = new();
    private EventHandler? _changed;
    private FileSystemWatcher? _watcher;
    private Timer? _timer;
    private bool _disposed;

    public JsonSettingsStore() : this(DefaultDirectory()) { }

    public JsonSettingsStore(string directory) : this(directory, TimeSpan.FromMilliseconds(300)) { }

    public JsonSettingsStore(string directory, TimeSpan debounce)
    {
        _directory = directory;
        _filePath = Path.Combine(directory, FileName);
        _debounce = debounce;
    }

    public static string DefaultDirectory()
    {
        var local = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);
        return Path.Combine(local, "OtzariaWidget");
    }

    public string SettingsFilePath => _filePath;

    public event EventHandler? Changed
    {
        add
        {
            lock (_gate)
            {
                _changed += value;
                EnsureWatcher();
            }
        }
        remove
        {
            lock (_gate) { _changed -= value; }
        }
    }

    // ------------------------------------------------------------------------------------------
    public WidgetAppSettings Load()
    {
        string? text = null;
        var notFound = 0;
        for (var attempt = 0; attempt < 8; attempt++)
        {
            try
            {
                using var fs = new FileStream(_filePath, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete);
                using var sr = new StreamReader(fs, new System.Text.UTF8Encoding(false), true);
                text = sr.ReadToEnd();
                break;
            }
            catch (DirectoryNotFoundException) { return new WidgetAppSettings(); }
            catch (FileNotFoundException)
            {
                // Either never saved, or a concurrent Save is swapping the file (File.Replace): retry briefly.
                // Once we have read a valid file before, wait longer before concluding that it really is gone.
                notFound++;
                if (notFound >= (_lastGoodText is null ? 3 : 8)) { _lastGoodText = null; return new WidgetAppSettings(); }
                Thread.Sleep(15);
            }
            catch (IOException) { Thread.Sleep(40); }
            catch (UnauthorizedAccessException) { Thread.Sleep(40); }
        }
        // Could not read (locked by someone else for too long): keep serving the last good settings instead of resetting to defaults.
        if (text is null) return ParseOrDefaults(_lastGoodText, backupOnFailure: false);

        var parsed = ParseOrDefaults(text, backupOnFailure: true, out var ok);
        if (ok) _lastGoodText = text;
        return parsed;
    }

    private volatile string? _lastGoodText;

    private WidgetAppSettings ParseOrDefaults(string? text, bool backupOnFailure) => ParseOrDefaults(text, backupOnFailure, out _);

    private WidgetAppSettings ParseOrDefaults(string? text, bool backupOnFailure, out bool ok)
    {
        ok = false;
        if (string.IsNullOrWhiteSpace(text))
        {
            if (backupOnFailure && text is not null) TryBackupCorrupt();
            return new WidgetAppSettings();
        }
        try
        {
            var s = JsonSerializer.Deserialize<WidgetAppSettings>(text, JsonOptions);
            if (s is not null) { ok = true; return s; }
        }
        catch (Exception) { /* fall through to the per-property recovery */ }

        // A single wrong value (unknown enum name, "true" as a string, ...) must not reset every other setting.
        if (backupOnFailure) TryBackupCorrupt();
        return RecoverPerProperty(text);
    }

    private static WidgetAppSettings RecoverPerProperty(string text)
    {
        var result = new WidgetAppSettings();
        try
        {
            using var doc = JsonDocument.Parse(text, new JsonDocumentOptions { AllowTrailingCommas = true, CommentHandling = JsonCommentHandling.Skip });
            if (doc.RootElement.ValueKind != JsonValueKind.Object) return result;
            foreach (var prop in typeof(WidgetAppSettings).GetProperties(System.Reflection.BindingFlags.Public | System.Reflection.BindingFlags.Instance))
            {
                if (!prop.CanWrite) continue;
                foreach (var jp in doc.RootElement.EnumerateObject())
                {
                    if (!string.Equals(jp.Name, prop.Name, StringComparison.OrdinalIgnoreCase)) continue;
                    try
                    {
                        var v = JsonSerializer.Deserialize(jp.Value.GetRawText(), prop.PropertyType, JsonOptions);
                        // a JSON null must not overwrite a non-nullable default
                        if (v is not null || Nullable.GetUnderlyingType(prop.PropertyType) is not null || !prop.PropertyType.IsValueType)
                            prop.SetValue(result, v);
                    }
                    catch (Exception) { /* keep the default for this property */ }
                    break;
                }
            }
        }
        catch (Exception) { /* corrupt document: defaults */ }
        return result;
    }

    public void Save(WidgetAppSettings settings)
    {
        ArgumentNullException.ThrowIfNull(settings);
        Directory.CreateDirectory(_directory);
        var json = JsonSerializer.Serialize(settings, JsonOptions);
        var tmp = Path.Combine(_directory, $"{FileName}.{Environment.ProcessId}.{Guid.NewGuid():N}.tmp");
        try
        {
            File.WriteAllText(tmp, json, new System.Text.UTF8Encoding(false));
            Exception? last = null;
            for (var attempt = 0; attempt < 5; attempt++)
            {
                try
                {
                    if (File.Exists(_filePath)) File.Replace(tmp, _filePath, null);
                    else File.Move(tmp, _filePath);
                    last = null;
                    break;
                }
                catch (IOException ex) { last = ex; Thread.Sleep(30 * (attempt + 1)); }
                catch (UnauthorizedAccessException ex) { last = ex; Thread.Sleep(30 * (attempt + 1)); }
            }
            if (last is not null) throw last;
        }
        finally
        {
            try { if (File.Exists(tmp)) File.Delete(tmp); } catch { }
        }
    }

    private void TryBackupCorrupt()
    {
        try { File.Copy(_filePath, _filePath + ".bak", overwrite: true); } catch { }
    }

    // ------------------------------------------------------------------------------------------
    private void EnsureWatcher()
    {
        if (_watcher is not null || _disposed) return;
        try
        {
            Directory.CreateDirectory(_directory);
            var w = new FileSystemWatcher(_directory, FileName)
            {
                NotifyFilter = NotifyFilters.LastWrite | NotifyFilters.FileName | NotifyFilters.Size | NotifyFilters.CreationTime,
                IncludeSubdirectories = false,
            };
            w.Changed += OnFsEvent;
            w.Created += OnFsEvent;
            w.Deleted += OnFsEvent;
            w.Renamed += OnFsEvent;
            w.EnableRaisingEvents = true;
            _watcher = w;
            _timer = new Timer(_ => Fire(), null, Timeout.Infinite, Timeout.Infinite);
        }
        catch
        {
            _watcher = null; // watching is best-effort
        }
    }

    private void OnFsEvent(object sender, FileSystemEventArgs e)
    {
        lock (_gate)
        {
            if (_disposed) return;
            _timer?.Change(_debounce, Timeout.InfiniteTimeSpan);
        }
    }

    private void Fire()
    {
        EventHandler? h;
        lock (_gate)
        {
            if (_disposed) return;
            h = _changed;
        }
        try { h?.Invoke(this, EventArgs.Empty); } catch { /* subscriber errors never break the store */ }
    }

    public void Dispose()
    {
        lock (_gate)
        {
            if (_disposed) return;
            _disposed = true;
            _watcher?.Dispose();
            _timer?.Dispose();
            _watcher = null;
            _timer = null;
        }
    }
}
