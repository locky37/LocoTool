using LocoTool.Core.Abstractions;
using LocoTool.Service;

namespace LocoTool.Core.Services;

/// <summary>
/// Adapter to wrap existing RestTranslateClient under ITranslateClient.
/// </summary>
public sealed class RestTranslateClientAdapter : ITranslateClient
{
    private readonly Func<RestTranslateClient> _factory;

    public RestTranslateClientAdapter(Func<RestTranslateClient> factory)
    {
        _factory = factory;
    }

    public async Task<IReadOnlyList<string>> TranslateBatchAsync(
        IEnumerable<string> texts,
        string target,
        string? source,
        IEnumerable<(string src, string dst, bool exact)>? glossary,
        bool speller,
        CancellationToken cancellationToken = default)
    {
        // Note: RestTranslateClient does not accept CancellationToken currently.
        var client = _factory();
        return await client.TranslateBatchAsync(texts, target, source, glossary, speller).ConfigureAwait(false);
    }
}

