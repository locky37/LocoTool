using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using LocoTool.Core.Abstractions;

namespace LocoTool.Core.Services;

public sealed class BatchCache : IBatchCache
{
    private readonly string _path;
    private readonly Dictionary<string, string[]> _map;

    public BatchCache(string path)
    {
        _path = path;
        try
        {
            _map = File.Exists(path)
                ? (JsonSerializer.Deserialize<Dictionary<string, string[]>>(File.ReadAllText(path)) ?? new())
                : new();
        }
        catch { _map = new(); }
    }

    public bool TryGet(string batchKey, out IReadOnlyList<string> translations)
    {
        if (_map.TryGetValue(batchKey, out var arr)) { translations = arr; return true; }
        translations = Array.Empty<string>();
        return false;
    }

    public void Put(string batchKey, IReadOnlyList<string> translations)
    {
        _map[batchKey] = translations.Select(s => s ?? string.Empty).ToArray();
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

    public static string ComputeKey(IEnumerable<string> texts, string target, string? source)
    {
        using var sha = SHA256.Create();
        var payload = string.Join("\n", texts) + "|" + target + "|" + (source ?? "");
        var hash = sha.ComputeHash(Encoding.UTF8.GetBytes(payload));
        return Convert.ToHexString(hash);
    }
}

