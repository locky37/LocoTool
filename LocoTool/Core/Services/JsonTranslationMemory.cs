using System.Text.Json;
using LocoTool.Core.Abstractions;

namespace LocoTool.Core.Services;

public sealed class JsonTranslationMemory : ITranslationMemory
{
    private readonly string _path;
    private readonly Dictionary<string, string> _map;

    public JsonTranslationMemory(string path)
    {
        _path = path;
        try
        {
            if (File.Exists(path))
            {
                var json = File.ReadAllText(path);
                _map = JsonSerializer.Deserialize<Dictionary<string, string>>(json) ?? new();
            }
            else _map = new();
        }
        catch { _map = new(); }
    }

    public bool TryGet(string src, out string dst) => _map.TryGetValue(src, out dst!);

    public void Add(string src, string dst)
    {
        if (!string.IsNullOrEmpty(src)) _map[src] = dst ?? string.Empty;
    }

    public void Save()
    {
        try
        {
            var dir = Path.GetDirectoryName(_path);
            if (!string.IsNullOrEmpty(dir)) Directory.CreateDirectory(dir);
            File.WriteAllText(_path, JsonSerializer.Serialize(_map));
        }
        catch { }
    }
}

