namespace LocoTool.Core.Abstractions;

/// <summary>Loads glossary and enforces limits.</summary>
public interface IGlossaryService
{
    (string src, string dst, bool exact)[] Load(string? path);
    (string src, string dst, bool exact)[] EnforceLimit((string src, string dst, bool exact)[] pairs, int maxPairs);
}

