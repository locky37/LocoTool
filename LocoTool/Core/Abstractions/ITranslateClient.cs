namespace LocoTool.Core.Abstractions;

/// <summary>Batch translate API abstraction.</summary>
public interface ITranslateClient
{
    Task<IReadOnlyList<string>> TranslateBatchAsync(
        IEnumerable<string> texts,
        string target,
        string? source,
        IEnumerable<(string src, string dst, bool exact)>? glossary,
        bool speller,
        CancellationToken cancellationToken = default);
}

