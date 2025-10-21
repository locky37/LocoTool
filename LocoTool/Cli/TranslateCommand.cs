using LocoTool.Config;
using LocoTool.Core.Abstractions;

namespace LocoTool.Cli;

/// <summary>'translate' command: read table, batch translate, write out.</summary>
public sealed class TranslateCommand : ICommandRunner
{
    private readonly IConfigService _config;
    private readonly IGlossaryService _glossary;
    private readonly ITableIo _tableIo;
    private readonly IStatsService _stats;

    public TranslateCommand(IConfigService config, IGlossaryService glossary, ITranslateClient _ignored, ITableIo tableIo, IStatsService stats)
    { _config = config; _glossary = glossary; _tableIo = tableIo; _stats = stats; }

    public string Name => "translate";

    public async Task<int> RunAsync(CommandContext context, CancellationToken cancellationToken)
    {
        var cfgRes = _config.Load(context.ConfigPath);
        if (!cfgRes.Success || cfgRes.Value is null)
        { Console.WriteLine($"[Config] {cfgRes.Error}"); return 1; }
        var cfg = cfgRes.Value;

        string tableIn = context.Args.Length > 1 ? context.Args[1] : "strings.tsv";
        string tableOut = context.Args.Length > 2 ? context.Args[2] : "strings_out.tsv";
        char delim = context.Delimiter;

        var (totalChars, stringsCount) = _stats.ComputeFromTable(tableIn, delim);
        var (batches, exactCost, paddedCost, paddedChars) = _stats.EstimateCost(totalChars, cfg.Limits.MaxCharsPerRequest, context.PricePerMillion);
        Console.WriteLine($"[stats] строк к переводу: {stringsCount}");
        Console.WriteLine($"[stats] всего символов: {totalChars:N0}");
        Console.WriteLine($"[stats] пачек по {cfg.Limits.MaxCharsPerRequest} симв.: {batches:N0}");
        if (exactCost is not null && paddedCost is not null)
        {
            Console.WriteLine($"[stats] оценка (по символам): ~{exactCost:0.00}");
            Console.WriteLine($"[stats] оценка (по пачкам):  ~{paddedCost:0.00}  (учтено {paddedChars:N0} симв.)");
        }

        var glossary = _glossary.Load(context.GlossaryPath ?? cfg.Yandex.GlossaryPath);
        glossary = _glossary.EnforceLimit(glossary, cfg.Limits.MaxGlossaryPairs);

        var rows = _tableIo.ReadRows(tableIn, delim).ToList();
        var client = new LocoTool.Core.Services.RestTranslateClientAdapter(
            () => new LocoTool.Service.RestTranslateClient(new HttpClient(), cfg.Yandex.AuthHeader, cfg.Yandex.FolderId));
        var toTranslateIdx = rows
            .Select((r, i) => (r, i))
            .Where(x => !string.IsNullOrWhiteSpace(x.r.OrigText) && string.IsNullOrWhiteSpace(x.r.TranslatedText))
            .Select(x => x.i)
            .ToList();

        var batch = new List<int>();
        int sum = 0;
        async Task FlushAsync()
        {
            if (batch.Count == 0) return;
            var texts = batch.Select(i => rows[i].OrigText).ToArray();
            var translated = await client.TranslateBatchAsync(texts, cfg.Yandex.DefaultTargetLang, cfg.Yandex.DefaultSourceLang, glossary, false, cancellationToken).ConfigureAwait(false);
            for (int j = 0; j < batch.Count; j++)
                rows[batch[j]] = rows[batch[j]] with { TranslatedText = translated[j] };
            Console.WriteLine($"  [translate] batch {batch.Count} strings, chars: {sum}");
            batch.Clear(); sum = 0;
        }

        foreach (var idx in toTranslateIdx)
        {
            var add = rows[idx].OrigText?.Length ?? 0;
            if (sum + add > cfg.Limits.MaxCharsPerRequest)
                await FlushAsync();
            batch.Add(idx);
            sum += add;
        }
        await FlushAsync();

        _tableIo.WriteRows(tableOut, delim, rows);
        Console.WriteLine($"[translate] OK -> {tableOut}");
        return 0;
    }
}
