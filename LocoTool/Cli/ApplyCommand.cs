using System.Text;
using LocoTool.Config;
using LocoTool.Core.Abstractions;

namespace LocoTool.Cli;

/// <summary>'apply' command: apply translated table to original input.</summary>
public sealed class ApplyCommand : ICommandRunner
{
    private readonly IParsingService _parsing;
    private readonly IConfigService _config;

    public ApplyCommand(IParsingService parsing, IConfigService config) { _parsing = parsing; _config = config; }

    public string Name => "apply";

    public Task<int> RunAsync(CommandContext context, CancellationToken cancellationToken)
    {
        var cfgRes = _config.Load(context.ConfigPath);
        if (!cfgRes.Success || cfgRes.Value is null)
        { Console.WriteLine($"[Config] {cfgRes.Error}"); return Task.FromResult(1); }
        var cfg = cfgRes.Value;

        string inputPath = context.Args.Length > 1 ? context.Args[1] : cfg.Files.DefaultInput;
        string tablePath = context.Args.Length > 2 ? context.Args[2] : "strings.tsv";
        string outputPath = context.Args.Length > 3 ? context.Args[3] : cfg.Files.DefaultOutput;

        string input = File.ReadAllText(inputPath, Encoding.UTF8);
        string tableText = File.ReadAllText(tablePath, Encoding.UTF8);
        string? sample = File.Exists(inputPath) ? File.ReadLines(inputPath).FirstOrDefault() : null;

        try
        {
            var output = _parsing.Apply(
                input,
                tableText,
                context.Delimiter,
                context.ApplyEmpty,
                context.ParserName ?? cfg.Parsers.Default,
                inputPath,
                sample,
                cfg.Parsers.Folder,
                cfg.Parsers.Assemblies);

            File.WriteAllText(outputPath, output, Encoding.UTF8);
            Console.WriteLine($"[apply] OK -> {outputPath}");
            return Task.FromResult(0);
        }
        catch (Exception ex)
        {
            Console.WriteLine(ex.Message);
            return Task.FromResult(1);
        }
    }
}

