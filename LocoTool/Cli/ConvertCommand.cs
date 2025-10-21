using LocoTool.Core.Services;

namespace LocoTool.Cli;

/// <summary>Converts TSV to HSV (#) with proper quoting.</summary>
public sealed class ConvertCommand : ICommandRunner
{
    public string Name => "convert";

    public Task<int> RunAsync(CommandContext context, CancellationToken cancellationToken)
    {
        if (context.Args.Length < 3)
        {
            Console.WriteLine("Usage: LocTool convert <in.tsv|in.hash> <out.hash>");
            return Task.FromResult(1);
        }
        var inPath = context.Args[1];
        var outPath = context.Args[2];

        if (!File.Exists(inPath)) { Console.WriteLine($"[convert] not found: {inPath}"); return Task.FromResult(1); }
        var lines = File.ReadAllLines(inPath);
        if (lines.Length == 0) { File.WriteAllText(outPath, ""); return Task.FromResult(0); }

        // Detect format by header
        var header = lines[0];
        char inDelim = header.Contains('#') ? '#' : '\t';
        var outDelim = '#';

        using var sw = new StreamWriter(outPath, false, System.Text.Encoding.UTF8);
        foreach (var line in lines)
        {
            var fields = inDelim == '#'
                ? HashCsv.ReadRow(line, '#')
                : line.Split('\t').ToList();
            sw.WriteLine(HashCsv.WriteRow(fields, outDelim));
        }
        Console.WriteLine($"[convert] TSV/HSV -> hash-csv: {outPath} (rows: {lines.Length})");
        return Task.FromResult(0);
    }
}

