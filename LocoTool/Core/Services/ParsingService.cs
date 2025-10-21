using LocoTool.Abstractions;
using LocoTool.Core.Abstractions;
using LocoTool.Core;

namespace LocoTool.Core.Services;

/// <summary>Facade for parser manager and parsing operations.</summary>
public sealed class ParsingService : IParsingService
{
    public ILocParser? Resolve(string? name, string? filePath, string? sample)
    {
        var pm = new ParserManager();
        // Load using default folder name; concrete calls below pass explicit folder.
        return pm.Resolve(name, filePath, sample);
    }

    public string Extract(
        string inputText,
        char delimiter,
        bool applyEmpty,
        string? parserName,
        string inputPath,
        string? sample,
        string parsersFolder,
        IEnumerable<string>? onlyAssemblies)
    {
        var pm = new ParserManager();
        pm.LoadFromFolder(Path.Combine(AppContext.BaseDirectory, parsersFolder), onlyAssemblies);
        var parser = pm.Resolve(parserName, inputPath, sample);
        if (parser is null)
            throw new InvalidOperationException("[parsers] Не найден подходящий парсер. Проверьте config.Parsers.* и DLL в /parsers");

        var options = new ParserOptions { TableDelimiter = delimiter, ApplyEmpty = applyEmpty, Extra = null };
        return parser.Extract(inputText, options);
    }

    public string Apply(
        string originalText,
        string tableText,
        char delimiter,
        bool applyEmpty,
        string? parserName,
        string inputPath,
        string? sample,
        string parsersFolder,
        IEnumerable<string>? onlyAssemblies)
    {
        var pm = new ParserManager();
        pm.LoadFromFolder(Path.Combine(AppContext.BaseDirectory, parsersFolder), onlyAssemblies);
        var parser = pm.Resolve(parserName, inputPath, sample);
        if (parser is null)
            throw new InvalidOperationException("[parsers] Не найден подходящий парсер. Проверьте config.Parsers.* и DLL в /parsers");

        var options = new ParserOptions { TableDelimiter = delimiter, ApplyEmpty = applyEmpty, Extra = null };
        return parser.Apply(originalText, tableText, options);
    }
}

