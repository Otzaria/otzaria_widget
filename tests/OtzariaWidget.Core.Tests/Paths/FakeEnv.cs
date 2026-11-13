using OtzariaWidget.Core.Paths;

namespace OtzariaWidget.Core.Tests.Paths;

/// <summary>In-memory file system / env / registry for path-resolution tests.</summary>
public sealed class FakeEnv : IResolverEnvironment
{
    private readonly HashSet<string> _files = new(StringComparer.OrdinalIgnoreCase);
    private readonly HashSet<string> _dirs = new(StringComparer.OrdinalIgnoreCase);
    private readonly Dictionary<string, string> _texts = new(StringComparer.OrdinalIgnoreCase);
    private readonly Dictionary<string, string> _env = new(StringComparer.OrdinalIgnoreCase);
    private readonly Dictionary<string, string> _reg = new(StringComparer.OrdinalIgnoreCase);

    public FakeEnv AddDir(string dir)
    {
        var d = dir.TrimEnd('\\');
        while (!string.IsNullOrEmpty(d))
        {
            _dirs.Add(d);
            d = Path.GetDirectoryName(d) ?? "";
        }
        return this;
    }

    public FakeEnv AddFile(string path, string? text = null)
    {
        _files.Add(path);
        AddDir(Path.GetDirectoryName(path)!);
        if (text is not null) _texts[path] = text;
        return this;
    }

    public FakeEnv SetEnv(string name, string value) { _env[name] = value; return this; }

    public FakeEnv SetReg(RegistryRoot root, string subKey, string? valueName, string value)
    {
        _reg[$"{root}|{subKey}|{valueName ?? ""}"] = value;
        return this;
    }

    public bool FileExists(string path) => _files.Contains(path);
    public bool DirectoryExists(string path) => _dirs.Contains(path.TrimEnd('\\'));
    public string? ReadAllText(string path) => _texts.TryGetValue(path, out var t) ? t : null;
    public string? GetEnvironmentVariable(string name) => _env.TryGetValue(name, out var v) ? v : null;
    public string? GetRegistryString(RegistryRoot root, string subKey, string? valueName)
        => _reg.TryGetValue($"{root}|{subKey}|{valueName ?? ""}", out var v) ? v : null;
}
