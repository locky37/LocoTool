using LocoTool.Core.Abstractions;

namespace LocoTool.Cli;

/// <summary>Implements 'stats' command: compute text and cost stats.</summary>
public sealed class StatsCommand : ICommandRunner
{
    private readonly IStatsService _stats;
    private readonly IConfigService _config;

    public StatsCommand(IStatsService stats, IConfigService config) { _stats = stats; _config = config; }

    public string Name => "stats";

    public Task<int> RunAsync(CommandContext context, CancellationToken cancellationToken)    {
        // stats <strings.hash|csv|tsv|dir> [--delimiter ...] [--price ...]
        string input = context.Args.Length > 1 ? context.Args[1] : "strings.tsv";

        long totalChars = 0;
        int stringsCount = 0;
        int filesCount = 0;

        if (Directory.Exists(input))
        {
            foreach (var pat in new[] { "*.hash", "*.tsv", "*.csv" })
            {
                foreach (var file in Directory.EnumerateFiles(input, pat, SearchOption.TopDirectoryOnly))
                {
                    var (t, s) = _stats.ComputeFromTable(file, context.Delimiter);
                    totalChars += t; stringsCount += s; filesCount++;
                }
            }
            Console.WriteLine($"[stats] источников: {filesCount}");
        }
        else
        {
            var (t, s) = _stats.ComputeFromTable(input, context.Delimiter);
            totalChars = t; stringsCount = s; filesCount = 1;
        }

        Console.WriteLine($"[stats] строк к переводу: {stringsCount}");
        Console.WriteLine($"[stats] всего символов: {totalChars:N0}");

        var cfgRes = _config.Load(context.ConfigPath);
        int maxPer = (cfgRes.Success && cfgRes.Value is not null) ? cfgRes.Value.Limits.MaxCharsPerRequest : 10000;

        var (batches, exactCost, paddedCost, paddedChars) = _stats.EstimateCost(
            totalChars,
            maxCharsPerRequest: maxPer,
            pricePerMillion: context.PricePerMillion);

        Console.WriteLine($"[stats] пачек по {maxPer} симв.: {batches:N0}");
        if (exactCost is not null && paddedCost is not null)
        {
            Console.WriteLine($"[stats] оценка (по символам): ~{exactCost:0.00}");
            Console.WriteLine($"[stats] оценка (по пачкам):  ~{paddedCost:0.00}  (учтено {paddedChars:N0} симв.)");
        }
        if (cfgRes.Success && cfgRes.Value is { GlobalTM.Enabled: true } cfg)
        {
            var gtm = new LocoTool.Core.Services.JsonlGlobalTranslationMemory(cfg.GlobalTM.RootPath, cfg.GlobalTM.Namespace, cfg.GlobalTM.PreferHumanEdited);
            var s = gtm.Stats();
            Console.WriteLine($"[gtm] hit-rate: {s.HitRate:P1} (hits: {s.Hits:N0} / misses: {s.Misses:N0}) entries: {s.Entries:N0} shards: {s.Shards:N0}");
        }

        return Task.FromResult(0);
    }}
