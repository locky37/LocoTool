using LocoTool.Core.Abstractions;

namespace LocoTool.Cli;

/// <summary>Implements 'stats' command: compute text and cost stats.</summary>
public sealed class StatsCommand : ICommandRunner
{
    private readonly IStatsService _stats;

    public StatsCommand(IStatsService stats) { _stats = stats; }

    public string Name => "stats";

    public Task<int> RunAsync(CommandContext context, CancellationToken cancellationToken)
    {
        // stats <strings.tsv|csv|hash> [--delimiter ...] [--price ...]
        string tableIn = context.Args.Length > 1 ? context.Args[1] : "strings.tsv";
        var (totalChars, stringsCount) = _stats.ComputeFromTable(tableIn, context.Delimiter);

        Console.WriteLine($"[stats] строк к переводу: {stringsCount}");
        Console.WriteLine($"[stats] всего символов: {totalChars:N0}");

        var (batches, exactCost, paddedCost, paddedChars) = _stats.EstimateCost(
            totalChars,
            maxCharsPerRequest: 10000, // default; real value is printed by All/Translate via config
            pricePerMillion: context.PricePerMillion);

        Console.WriteLine($"[stats] пачек по 10000 симв.: {batches:N0}");
        if (exactCost is not null && paddedCost is not null)
        {
            Console.WriteLine($"[stats] оценка (по символам): ~{exactCost:0.00}");
            Console.WriteLine($"[stats] оценка (по пачкам):  ~{paddedCost:0.00}  (учтено {paddedChars:N0} симв.)");
        }
        return Task.FromResult(0);
    }
}

