using LocoTool.Core.Abstractions;
using LocoTool.Service;

namespace LocoTool.Core.Services;

/// <summary>Glossary I/O and limiting.</summary>
public sealed class GlossaryService : IGlossaryService
{
    public (string src, string dst, bool exact)[] Load(string? path)
        => GlossaryLoader.Load(path);

    public (string src, string dst, bool exact)[] EnforceLimit((string src, string dst, bool exact)[] pairs, int maxPairs)
        => pairs.Length <= maxPairs ? pairs : pairs.Take(maxPairs).ToArray();
}

