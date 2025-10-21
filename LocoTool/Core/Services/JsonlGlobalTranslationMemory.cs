using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using LocoTool.Core.Abstractions;

namespace LocoTool.Core.Services;

/// <summary>
/// JSONL-backed global TM: shards by namespace/src-dst, keeps in-memory index for current shard.
/// Simplified implementation focusing on append/lookup/import/export and basic locking.
/// </summary>
public sealed class JsonlGlobalTranslationMemory : IGlobalTranslationMemory
{
    private readonly string _root;
    private readonly string _ns;
    private readonly bool _preferHuman;
    private readonly object _gate = new();
    private readonly Dictionary<string, List<Entry>> _index = new(StringComparer.Ordinal);
    private long _hits, _misses;

    public JsonlGlobalTranslationMemory(string rootPath, string @namespace, bool preferHumanEdited)
    {
        _root = ExpandHome(rootPath);
        _ns = string.IsNullOrWhiteSpace(@namespace) ? "default" : @namespace;
        _preferHuman = preferHumanEdited;
    }

    public bool TryGet(string src, string srcLang, string dstLang, string? context, out string dst)
    {
        var shard = GetShardPath(srcLang, dstLang);
        EnsureLoaded(shard);
        lock (_gate)
        {
            if (_index.TryGetValue(src, out var list) && list.Count > 0)
            {
                // Prefer humanEdited, then higher confidence
                var best = list
                    .OrderByDescending(e => _preferHuman && e.HumanEdited)
                    .ThenByDescending(e => e.Confidence)
                    .FirstOrDefault();
                if (best != null)
                {
                    _hits++;
                    dst = best.Dst;
                    return true;
                }
            }
        }
        _misses++;
        dst = string.Empty;
        return false;
    }

    public void Append(string src, string dst, string srcLang, string dstLang, double confidence, bool humanEdited, IEnumerable<string>? contexts = null, string? source = null)
    {
        var shard = GetShardPath(srcLang, dstLang);
        Directory.CreateDirectory(Path.GetDirectoryName(shard)!);
        var entry = new Entry
        {
            Src = src ?? string.Empty,
            Dst = dst ?? string.Empty,
            SrcLang = srcLang,
            DstLang = dstLang,
            CreatedUtc = DateTime.UtcNow,
            UpdatedUtc = DateTime.UtcNow,
            Source = source,
            Confidence = confidence,
            HumanEdited = humanEdited,
            Context = contexts?.ToArray() ?? Array.Empty<string>()
        };

        // Simple append with retry + atomic rename via temp file (to avoid partial writes)
        var tmp = shard + ".tmp";
        var line = JsonSerializer.Serialize(entry, JsonOptions) + "\n";
        lock (_gate)
        {
            File.AppendAllText(tmp, line, Encoding.UTF8);
            // concat existing shard and tmp if shard exists
            using (var outFs = new FileStream(shard, FileMode.Append, FileAccess.Write, FileShare.Read))
            using (var tmpFs = new FileStream(tmp, FileMode.Open, FileAccess.Read, FileShare.Read))
            {
                tmpFs.CopyTo(outFs);
            }
            File.Delete(tmp);

            if (!_index.TryGetValue(entry.Src, out var list))
                _index[entry.Src] = list = new List<Entry>();
            list.Add(entry);
        }
    }

    public void Merge(IEnumerable<(string src, string dst, string? context, double? confidence, bool? humanEdited)> entries)
    {
        foreach (var e in entries)
            Append(e.src, e.dst, "", "", e.confidence ?? 1.0, e.humanEdited ?? false, e.context is null ? null : new[] { e.context });
    }

    public void Import(string path)
    {
        if (!File.Exists(path)) return;
        var ext = Path.GetExtension(path).ToLowerInvariant();
        if (ext == ".jsonl")
        {
            foreach (var line in File.ReadLines(path, Encoding.UTF8))
            {
                if (string.IsNullOrWhiteSpace(line)) continue;
                var e = JsonSerializer.Deserialize<Entry>(line, JsonOptions);
                if (e is null) continue;
                Append(e.Src, e.Dst, e.SrcLang, e.DstLang, e.Confidence, e.HumanEdited, e.Context, e.Source);
            }
        }
        else if (ext == ".tsv")
        {
            using var sr = new StreamReader(path, Encoding.UTF8);
            var header = (sr.ReadLine() ?? "").Split('\t');
            int iSrc = Array.IndexOf(header, "src");
            int iDst = Array.IndexOf(header, "dst");
            int iSrcL = Array.IndexOf(header, "srcLang");
            int iDstL = Array.IndexOf(header, "dstLang");
            int iCtx = Array.IndexOf(header, "context");
            int iHe = Array.IndexOf(header, "humanEdited");
            int iConf = Array.IndexOf(header, "confidence");
            string? row;
            while ((row = sr.ReadLine()) != null)
            {
                var c = row.Split('\t');
                var ctx = iCtx >= 0 && iCtx < c.Length ? c[iCtx] : null;
                Append(c[iSrc], c[iDst], c[iSrcL], c[iDstL],
                    double.TryParse(iConf >= 0 && iConf < c.Length ? c[iConf] : "", out var conf) ? conf : 1.0,
                    bool.TryParse(iHe >= 0 && iHe < c.Length ? c[iHe] : "false", out var he) && he,
                    string.IsNullOrWhiteSpace(ctx) ? null : new[] { ctx });
            }
        }
        else if (ext == ".json")
        {
            var list = JsonSerializer.Deserialize<List<Entry>>(File.ReadAllText(path), JsonOptions) ?? new();
            foreach (var e in list)
                Append(e.Src, e.Dst, e.SrcLang, e.DstLang, e.Confidence, e.HumanEdited, e.Context, e.Source);
        }
    }

    public void Export(string path)
    {
        var ext = Path.GetExtension(path).ToLowerInvariant();
        var all = SnapshotAllEntries();
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        if (ext == ".jsonl")
        {
            using var sw = new StreamWriter(path, false, Encoding.UTF8);
            foreach (var e in all)
                sw.WriteLine(JsonSerializer.Serialize(e, JsonOptions));
        }
        else // tsv by default
        {
            using var sw = new StreamWriter(path, false, Encoding.UTF8);
            sw.WriteLine("src\tdst\tsrcLang\tdstLang\tcontext\thumanEdited\tconfidence");
            foreach (var e in all)
            {
                var ctx = e.Context is { Length: > 0 } ? string.Join('|', e.Context) : string.Empty;
                sw.WriteLine(string.Join('\t', new[] { e.Src, e.Dst, e.SrcLang, e.DstLang, ctx, e.HumanEdited.ToString(), e.Confidence.ToString("0.###") }));
            }
        }
    }

    public void Vacuum()
    {
        // Simplified: no-op placeholder; could rebuild shards/indices.
    }

    public GlobalTmStats Stats()
    {
        var entries = _index.Values.Sum(l => l.Count);
        var shards = Directory.Exists(GetNsRoot()) ? Directory.EnumerateFiles(GetNsRoot(), "*.tm.jsonl").Count() : 0;
        return new GlobalTmStats(_hits, _misses, entries, shards, 0);
    }

    private List<Entry> SnapshotAllEntries()
    {
        lock (_gate)
        {
            return _index.Values.SelectMany(l => l).ToList();
        }
    }

    private void EnsureLoaded(string shard)
    {
        if (!File.Exists(shard)) return;
        lock (_gate)
        {
            using var sr = new StreamReader(shard, Encoding.UTF8);
            string? line;
            while ((line = sr.ReadLine()) != null)
            {
                if (string.IsNullOrWhiteSpace(line)) continue;
                var e = JsonSerializer.Deserialize<Entry>(line, JsonOptions);
                if (e is null) continue;
                if (!_index.TryGetValue(e.Src, out var list))
                    _index[e.Src] = list = new List<Entry>();
                list.Add(e);
            }
        }
    }

    private string GetShardPath(string srcLang, string dstLang)
    {
        var dir = GetNsRoot();
        Directory.CreateDirectory(dir);
        return Path.Combine(dir, $"{srcLang}-{dstLang}.tm.jsonl");
    }

    private string GetNsRoot() => Path.Combine(_root, _ns);

    private static string ExpandHome(string path)
    {
        if (string.IsNullOrWhiteSpace(path)) return path;
        if (path.StartsWith("~\\") || path.StartsWith("~/"))
            return Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), path[2..]);
        if (path == "~") return Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
        return path;
    }

    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase
    };

    private sealed class Entry
    {
        public string Src { get; set; } = string.Empty;
        public string Dst { get; set; } = string.Empty;
        public string SrcLang { get; set; } = string.Empty;
        public string DstLang { get; set; } = string.Empty;
        public DateTime CreatedUtc { get; set; }
        public DateTime UpdatedUtc { get; set; }
        public string? Source { get; set; }
        public double Confidence { get; set; }
        public bool HumanEdited { get; set; }
        public string[] Context { get; set; } = Array.Empty<string>();
    }
}
