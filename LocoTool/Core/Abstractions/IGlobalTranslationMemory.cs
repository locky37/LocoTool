namespace LocoTool.Core.Abstractions;

/// <summary>Global, cross-project translation memory stored on disk (JSONL/TSV).</summary>
public interface IGlobalTranslationMemory
{
    bool TryGet(string src, string srcLang, string dstLang, string? context, out string dst);
    void Append(string src, string dst, string srcLang, string dstLang, double confidence, bool humanEdited, IEnumerable<string>? contexts = null, string? source = null);
    void Merge(IEnumerable<(string src, string dst, string? context, double? confidence, bool? humanEdited)> entries);
    void Import(string path); // json|jsonl|tsv
    void Export(string path); // jsonl|tsv
    void Vacuum();            // maintenance
    GlobalTmStats Stats();
}

public readonly record struct GlobalTmStats(long Hits, long Misses, long Entries, int Shards, double AvgLookupMs)
{
    public double HitRate => (Hits + Misses) == 0 ? 0 : (double)Hits / (Hits + Misses);
}

