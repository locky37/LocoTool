using System.Text;
using LocoTool.Config;
using LocoTool.Core.Abstractions;

namespace LocoTool.Cli;

/// <summary>Implements 'extract' command: parse input and write table.</summary>
public sealed class ExtractCommand : ICommandRunner
{
    private readonly IParsingService _parsing;
    private readonly ITableIo _tableIo;
    private readonly IConfigService _config;

    public ExtractCommand(IParsingService parsing, ITableIo tableIo, IConfigService config)
    { _parsing = parsing; _tableIo = tableIo; _config = config; }

    public string Name => "extract";

    public Task<int> RunAsync(CommandContext context, CancellationToken cancellationToken)
    {
        var cfgRes = _config.Load(context.ConfigPath);
        if (!cfgRes.Success || cfgRes.Value is null)
        { Console.WriteLine($"[Config] {cfgRes.Error}"); return Task.FromResult(1); }
        var cfg = cfgRes.Value;

        string inputPath = context.Args.Length > 1 ? context.Args[1] : cfg.Files.DefaultInput;
        string tableOut = context.Args.Length > 2 ? context.Args[2] : "strings.tsv";

        string input = File.ReadAllText(inputPath, Encoding.UTF8);
        string? sample = File.Exists(inputPath) ? File.ReadLines(inputPath).FirstOrDefault() : null;

        try
        {
            var effectiveParser = context.ParserName ?? cfg.Parsers.Default;
            var table = _parsing.Extract(
                input,
                context.Delimiter,
                context.ApplyEmpty,
                effectiveParser,
                inputPath,
                sample,
                cfg.Parsers.Folder,
                cfg.Parsers.Assemblies);
            var isHashPlus = string.Equals(effectiveParser, "hashplus", StringComparison.OrdinalIgnoreCase);
            var outLooksDir = LooksLikeDirectory(tableOut);
            if (isHashPlus)
            {
                var groups = GroupByRecordId(table, context.Delimiter);
                if (outLooksDir)
                {
                    var dir = tableOut;
                    Directory.CreateDirectory(dir);
                    var prefix = Path.GetFileNameWithoutExtension(inputPath);
                    var ext = ".tsv";
                    foreach (var (tag, content) in groups)
                    {
                        var outPath = Path.Combine(dir, $"{prefix}+{tag}{ext}");
                        File.WriteAllText(outPath, content, Encoding.UTF8);
                        Console.WriteLine($"[extract] OK -> {outPath}");
                    }
                }
                else
                {
                    var dir = Path.GetDirectoryName(tableOut) ?? Environment.CurrentDirectory;
                    var baseName = Path.GetFileNameWithoutExtension(tableOut);
                    var ext = Path.GetExtension(tableOut);
                    if (string.IsNullOrEmpty(ext)) ext = ".tsv";
                    foreach (var (tag, content) in groups)
                    {
                        var outPath = Path.Combine(dir, $"{baseName}+{tag}{ext}");
                        File.WriteAllText(outPath, content, Encoding.UTF8);
                        Console.WriteLine($"[extract] OK -> {outPath}");
                    }
                }
            }
            else
            {
                // Non-splitting parsers keep single-file behavior; if tableOut is a directory, use default name
                if (outLooksDir)
                {
                    Directory.CreateDirectory(tableOut);
                    var name = Path.GetFileNameWithoutExtension(inputPath);
                    var outPath = Path.Combine(tableOut, $"{name}.tsv");
                    File.WriteAllText(outPath, table, Encoding.UTF8);
                    Console.WriteLine($"[extract] OK -> {outPath}");
                }
                else
                {
                    File.WriteAllText(tableOut, table, Encoding.UTF8);
                    Console.WriteLine($"[extract] OK -> {tableOut}");
                }
            }
            return Task.FromResult(0);
        }
        catch (Exception ex)
        {
            Console.WriteLine(ex.Message);
            return Task.FromResult(1);
        }
    }

    private static List<(string tag, string content)> GroupByRecordId(string tsv, char delim)
    {
        var lines = tsv.Split('\n');
        if (lines.Length == 0) return new();
        var header = lines[0].TrimEnd('\r');
        var cols = header.Split(delim);
        int iTag = Array.IndexOf(cols, "record_id_guess");
        if (iTag < 0) iTag = 2; // fallback to default column index

        var dict = new Dictionary<string, List<string>>(StringComparer.OrdinalIgnoreCase);
        for (int i = 1; i < lines.Length; i++)
        {
            var l = lines[i].TrimEnd('\r');
            if (string.IsNullOrWhiteSpace(l)) continue;
            var c = l.Split(delim);
            var tag = (iTag >= 0 && iTag < c.Length) ? c[iTag] : "_";
            if (!dict.TryGetValue(tag, out var list))
            {
                list = new List<string> { header };
                dict[tag] = list;
            }
            list.Add(l);
        }
        return dict.Select(kv => (kv.Key, string.Join(Environment.NewLine, kv.Value))).ToList();
    }

    private static bool LooksLikeDirectory(string path)
    {
        if (string.IsNullOrWhiteSpace(path)) return false;
        if (Directory.Exists(path)) return true;
        // Heuristic: no extension -> likely directory intention
        return string.IsNullOrEmpty(Path.GetExtension(path));
    }
}
