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
    private readonly ITranslateClient? _client;
    private readonly IDeduplicator _dedup = new LocoTool.Core.Services.Deduplicator();
    private readonly IBatchPlanner _planner = new LocoTool.Core.Services.BatchPlanner();
    private readonly IPlaceholderService _ph = new LocoTool.Core.Services.PlaceholderService();

    public TranslateCommand(IConfigService config, IGlossaryService glossary, ITranslateClient? client, ITableIo tableIo, IStatsService stats)
    { _config = config; _glossary = glossary; _tableIo = tableIo; _stats = stats; _client = client; }

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

        var glossary = _glossary.Load(context.GlossaryPath ?? cfg.Yandex.GlossaryPath);
        glossary = _glossary.EnforceLimit(glossary, cfg.Limits.MaxGlossaryPairs);

        if (Directory.Exists(tableIn))
        {
            var inDir = tableIn;
            var outDir = tableOut;
            if (!Directory.Exists(outDir)) Directory.CreateDirectory(outDir);

            var files = Directory.EnumerateFiles(inDir, "*.tsv");
            foreach (var file in files)
            {
                var outPath = Path.Combine(outDir, Path.GetFileName(file));
                await TranslateOneAsync(file, outPath).ConfigureAwait(false);
                Console.WriteLine($"[translate] OK -> {outPath}");
            }
            return 0;
        }
        else
        {
            // Single file or file-based batch (prefix+tag.tsv -> process same-prefix files)
            if (File.Exists(tableIn))
            {
                var (dir, files) = ResolveBatchBySelectedFile(tableIn);
                if (files.Count > 1 || string.IsNullOrEmpty(Path.GetExtension(tableOut)))
                {
                    var outDir = tableOut;
                    Directory.CreateDirectory(outDir);
                    foreach (var file in files)
                    {
                        var outPath = Path.Combine(outDir, Path.GetFileName(file));
                        await TranslateOneAsync(file, outPath).ConfigureAwait(false);
                        Console.WriteLine($"[translate] OK -> {outPath}");
                    }
                    return 0;
                }
                else
                {
                    // Exactly one file selected and output is a file path
                    await TranslateOneAsync(tableIn, tableOut).ConfigureAwait(false);
                    Console.WriteLine($"[translate] OK -> {tableOut}");
                    return 0;
                }
            }
            else
            {
                // If output looks like directory intention, create and write inside
                if (string.IsNullOrEmpty(Path.GetExtension(tableOut)))
                {
                    var outDir = tableOut;
                    Directory.CreateDirectory(outDir);
                    var outPath = Path.Combine(outDir, Path.GetFileName(tableIn));
                    await TranslateOneAsync(tableIn, outPath).ConfigureAwait(false);
                    Console.WriteLine($"[translate] OK -> {outPath}");
                    return 0;
                }
                await TranslateOneAsync(tableIn, tableOut).ConfigureAwait(false);
                Console.WriteLine($"[translate] OK -> {tableOut}");
                return 0;
            }
        }

        async Task TranslateOneAsync(string inputTable, string outputTable)
        {
            var (totalChars, stringsCount) = _stats.ComputeFromTable(inputTable, delim);
            var (batches, exactCost, paddedCost, paddedChars) = _stats.EstimateCost(totalChars, cfg.Limits.MaxCharsPerRequest, context.PricePerMillion);
            Console.WriteLine($"[stats] строк к переводу: {stringsCount}");
            Console.WriteLine($"[stats] всего символов: {totalChars:N0}");
            Console.WriteLine($"[stats] пачек по {cfg.Limits.MaxCharsPerRequest} симв.: {batches:N0}");
            if (exactCost is not null && paddedCost is not null)
            {
                Console.WriteLine($"[stats] оценка (по символам): ~{exactCost:0.00}");
                Console.WriteLine($"[stats] оценка (по пачкам):  ~{paddedCost:0.00}  (учтено {paddedChars:N0} симв.)");
            }

            var rows = _tableIo.ReadRows(inputTable, delim).ToList();
            var client = _client ?? new LocoTool.Core.Services.RestTranslateClientAdapter(
                () => new LocoTool.Service.RestTranslateClient(new HttpClient(), cfg.Yandex.AuthHeader, cfg.Yandex.FolderId));

            var toTranslateIdx = rows
                .Select((r, i) => (r, i))
                .Where(x => !string.IsNullOrWhiteSpace(x.r.OrigText) && string.IsNullOrWhiteSpace(x.r.TranslatedText))
                .Select(x => x.i)
                .ToList();

            // Optimization services
            ITranslationMemory? tm = null;
            if (context.OptUseTm || cfg.Optimization.UseTM)
                tm = new LocoTool.Core.Services.JsonTranslationMemory(context.OptTmPath ?? cfg.Optimization.TMPath);
            IBatchCache? cache = null;
            if (context.OptBatchCache || cfg.Optimization.BatchCache)
                cache = new LocoTool.Core.Services.BatchCache(Path.Combine(Path.GetDirectoryName(outputTable) ?? Environment.CurrentDirectory, "batchcache.json"));

            // Build list of texts to translate, applying TM and placeholders if enabled
            var toTranslate = new List<int>();
            var masked = new Dictionary<int, string[]>();
            foreach (var idx in toTranslateIdx)
            {
                var text = rows[idx].OrigText ?? string.Empty;
                if (tm != null && tm.TryGet(text, out var cached))
                {
                    rows[idx] = rows[idx] with { TranslatedText = cached };
                    continue;
                }
                if (context.OptPlaceholders || cfg.Optimization.Placeholders)
                {
                    text = _ph.Mask(text, out var phs);
                    masked[idx] = phs;
                    // temporarily store masked into rows? keep local only
                }
                toTranslate.Add(idx);
            }

            // Deduplicate
            var textsToTranslate = toTranslate.Select(i => rows[i].OrigText ?? string.Empty).ToList();
            if (context.OptPlaceholders || cfg.Optimization.Placeholders)
            {
                for (int k = 0; k < toTranslate.Count; k++)
                {
                    var idx = toTranslate[k];
                    if (masked.TryGetValue(idx, out var phs))
                    {
                        textsToTranslate[k] = _ph.Mask(rows[idx].OrigText ?? string.Empty, out _); // ensure masked used
                    }
                }
            }

            var (unique, map) = (context.OptDedup || cfg.Optimization.Deduplicate)
                ? _dedup.Deduplicate(textsToTranslate)
                : (textsToTranslate, Enumerable.Range(0, textsToTranslate.Count).ToArray());

            // Plan batches
            var plannedBatches = _planner.Plan(unique, cfg.Limits.MaxCharsPerRequest);
            foreach (var b in plannedBatches)
            {
                var texts = b.Select(i => unique[i]).ToArray();
                IReadOnlyList<string> translated;
                var key = cache != null ? LocoTool.Core.Services.BatchCache.ComputeKey(texts, cfg.Yandex.DefaultTargetLang, cfg.Yandex.DefaultSourceLang) : null;
                if (key != null && cache!.TryGet(key, out translated))
                {
                    // from cache
                }
                else
                {
                    translated = await client.TranslateBatchAsync(texts, cfg.Yandex.DefaultTargetLang, cfg.Yandex.DefaultSourceLang, glossary, false, cancellationToken).ConfigureAwait(false);
                    if (key != null) cache!.Put(key!, translated);
                }

                for (int j = 0; j < b.Count; j++)
                {
                    var uniqIdx = b[j];
                    var translatedText = translated[j] ?? string.Empty;
                    // propagate to all mapped originals
                    for (int k = 0; k < map.Length; k++)
                    {
                        if (map[k] == uniqIdx)
                        {
                            var rowIndex = toTranslate[k];
                            if (masked.TryGetValue(rowIndex, out var phs))
                                translatedText = _ph.Unmask(translatedText, phs);
                            rows[rowIndex] = rows[rowIndex] with { TranslatedText = translatedText };
                            if (tm != null)
                                tm.Add(rows[rowIndex].OrigText ?? string.Empty, translatedText);
                        }
                    }
                }
            }
            tm?.Save();
            cache?.Save();

            _tableIo.WriteRows(outputTable, delim, rows);
            if (context.OptHumanLoop || cfg.Optimization.HumanLoop)
            {
                var reviewPath = Path.Combine(Path.GetDirectoryName(outputTable) ?? Environment.CurrentDirectory, "review.tsv");
                var items = rows.Select(r => (r.OrigText ?? string.Empty, r.TranslatedText ?? string.Empty));
                new LocoTool.Core.Services.HumanLoopService().ExportReview(reviewPath, items);
            }
        }

        static (string dir, List<string> files) ResolveBatchBySelectedFile(string selectedPath)
        {
            var dir = Path.GetDirectoryName(selectedPath) ?? Environment.CurrentDirectory;
            var name = Path.GetFileName(selectedPath);
            var baseName = Path.GetFileNameWithoutExtension(name);
            var plusIdx = baseName.IndexOf('+');
            if (plusIdx > 0)
            {
                var prefix = baseName[..plusIdx];
                var all = Directory.EnumerateFiles(dir, prefix + "+*.tsv", SearchOption.TopDirectoryOnly).ToList();
                if (all.Count > 0) return (dir, all);
            }
            return (dir, new List<string> { selectedPath });
        }
    }
}
