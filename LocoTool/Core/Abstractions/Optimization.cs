namespace LocoTool.Core.Abstractions;

/// <summary>Finds unique segments and maps duplicates to uniques.</summary>
public interface IDeduplicator
{
    (List<string> unique, int[] mapToUnique) Deduplicate(IReadOnlyList<string> segments);
}

/// <summary>Local translation memory.</summary>
public interface ITranslationMemory
{
    bool TryGet(string src, out string dst);
    void Add(string src, string dst);
    void Save();
}

/// <summary>Plans batches of indices under char constraints.</summary>
public interface IBatchPlanner
{
    List<List<int>> Plan(IReadOnlyList<string> texts, int maxCharsPerRequest);
}

/// <summary>Replaces placeholders (e.g., numbers) with tokens and restores after translation.</summary>
public interface IPlaceholderService
{
    string Mask(string input, out string[] placeholders);
    string Unmask(string translated, string[] placeholders);
}

/// <summary>Persists and reuses batch translations by content hash.</summary>
public interface IBatchCache
{
    bool TryGet(string batchKey, out IReadOnlyList<string> translations);
    void Put(string batchKey, IReadOnlyList<string> translations);
    void Save();
}

/// <summary>Human-in-the-loop artifacts (review export).</summary>
public interface IHumanLoop
{
    void ExportReview(string path, IEnumerable<(string orig, string mtSuggest)> items);
}

