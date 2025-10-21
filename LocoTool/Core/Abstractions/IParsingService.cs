using LocoTool.Abstractions;

namespace LocoTool.Core.Abstractions;

/// <summary>Facade over parser discovery and operations.</summary>
public interface IParsingService
{
    ILocParser? Resolve(string? name, string? filePath, string? sample);
    string Extract(string inputText, char delimiter, bool applyEmpty, string? parserName, string inputPath, string? sample, string parsersFolder, IEnumerable<string>? onlyAssemblies);
    string Apply(string originalText, string tableText, char delimiter, bool applyEmpty, string? parserName, string inputPath, string? sample, string parsersFolder, IEnumerable<string>? onlyAssemblies);
}

