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
            var table = _parsing.Extract(
                input,
                context.Delimiter,
                context.ApplyEmpty,
                context.ParserName ?? cfg.Parsers.Default,
                inputPath,
                sample,
                cfg.Parsers.Folder,
                cfg.Parsers.Assemblies);

            File.WriteAllText(tableOut, table, Encoding.UTF8);
            Console.WriteLine($"[extract] OK -> {tableOut}");
            return Task.FromResult(0);
        }
        catch (Exception ex)
        {
            Console.WriteLine(ex.Message);
            return Task.FromResult(1);
        }
    }
}

