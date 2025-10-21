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
            // Global TM
            var gtmEnabled = context.GtmEnabledOverride ?? cfg.GlobalTM.Enabled;
            IGlobalTranslationMemory? gtm = null;
            if (gtmEnabled)
            {
                var ns = context.GtmNamespace ?? cfg.GlobalTM.Namespace;
                gtm = new LocoTool.Core.Services.JsonlGlobalTranslationMemory(cfg.GlobalTM.RootPath, ns, cfg.GlobalTM.PreferHumanEdited);
                if (!string.IsNullOrWhiteSpace(context.GtmImport))
                    gtm.Import(context.GtmImport);
                if (!string.IsNullOrWhiteSpace(context.GtmExport))
                    gtm.Export(context.GtmExport);
            }
            IBatchCache? cache = null;
            if (context.OptBatchCache || cfg.Optimization.BatchCache)
                cache = new LocoTool.Core.Services.BatchCache(Path.Combine(Path.GetDirectoryName(outputTable) ?? Environment.CurrentDirectory, "batchcache.json"));

            // Build list of texts to translate, applying TM and placeholders if enabled
            var toTranslate = new List<int>();
            var masked = new Dictionary<int, string[]>();
            int gtmHits = 0;
            foreach (var idx in toTranslateIdx)
            {
                var text = rows[idx].OrigText ?? string.Empty;
                if (tm != null && tm.TryGet(text, out var cached))
                {
                    rows[idx] = rows[idx] with { TranslatedText = cached };
                    continue;
                }
                if (gtm != null)
                {
                    var first = context.GtmPriority;
                    var preferGlobal = string.Equals(first, "global", StringComparison.OrdinalIgnoreCase);
                    if (preferGlobal)
                    {
                        if (gtm.TryGet(text, cfg.Yandex.DefaultSourceLang, cfg.Yandex.DefaultTargetLang, null, out var hit))
                        { rows[idx] = rows[idx] with { TranslatedText = hit }; gtmHits++; continue; }
                    }
                    else
                    {
                        // already checked TM above; now GTM
                        if (gtm.TryGet(text, cfg.Yandex.DefaultSourceLang, cfg.Yandex.DefaultTargetLang, null, out var hit))
                        { rows[idx] = rows[idx] with { TranslatedText = hit }; gtmHits++; continue; }
                    }
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

            var dedupEnabled = (context.OptDedup || cfg.Optimization.Deduplicate);
            var (unique, map) = dedupEnabled
                ? _dedup.Deduplicate(textsToTranslate)
                : (textsToTranslate, Enumerable.Range(0, textsToTranslate.Count).ToArray());

            if (dedupEnabled)
            {
                var total = textsToTranslate.Count;
                var uniqueCount = unique.Count;
                var saved = Math.Max(0, total - uniqueCount);
                var pct = total > 0 ? (saved / (double)total) : 0.0;
                Console.WriteLine($"[dedup] unique: {uniqueCount} / total: {total} (saved {pct:P1})");

                try
                {
                    var outDir = Path.GetDirectoryName(outputTable) ?? Environment.CurrentDirectory;
                    var baseName = Path.GetFileNameWithoutExtension(outputTable);
                    var statsPath = Path.Combine(outDir, baseName + ".dedup.txt");
                    var lines = new List<string>
                    {
                        $"total={total}",
                        $"unique={uniqueCount}",
                        $"saved={saved}",
                        $"saved_pct={(pct*100.0):0.0}"
                    };
                    File.WriteAllLines(statsPath, lines);
                }
                catch { /* ignore IO issues for stats */ }
            }

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
                    if (context.OptBatchCache || cfg.Optimization.BatchJoin)
                    {
                        // Concat-join strategy
                        const string sep = "\u241F"; // unit separator visible char
                        var joined = new List<string>();
                        var mapping = new List<List<int>>();
                        var acc = new List<int>();
                        var accLen = 0;
                        for (int j = 0; j < texts.Length; j++)
                        {
                            var t = texts[j] ?? string.Empty;
                            var add = t.Length + (acc.Count == 0 ? 0 : sep.Length);
                            if (accLen + add > cfg.Optimization.MaxJoinChars && acc.Count > 0)
                            {
                                joined.Add(string.Join(sep, acc.Select(ix => texts[ix])));
                                mapping.Add(new List<int>(acc));
                                acc.Clear(); accLen = 0;
                            }
                            if (t.Length >= cfg.Optimization.MinLenToJoin && (accLen + add) <= cfg.Optimization.MaxJoinChars)
                            { acc.Add(j); accLen += add; }
                            else
                            {
                                joined.Add(t); mapping.Add(new List<int> { j });
                            }
                        }
                        if (acc.Count > 0) { joined.Add(string.Join(sep, acc.Select(ix => texts[ix]))); mapping.Add(new List<int>(acc)); }

                        var joinedTranslated = await client.TranslateBatchAsync(joined, cfg.Yandex.DefaultTargetLang, cfg.Yandex.DefaultSourceLang, glossary, false, cancellationToken).ConfigureAwait(false);
                        // split back
                        var result = new string[texts.Length];
                        for (int j = 0; j < mapping.Count; j++)
                        {
                            var parts = mapping[j];
                            if (parts.Count == 1)
                            {
                                result[parts[0]] = joinedTranslated[j];
                            }
                            else
                            {
                                var split = (joinedTranslated[j] ?? string.Empty).Split(sep);
                                for (int k = 0; k < parts.Count && k < split.Length; k++)
                                    result[parts[k]] = split[k];
                            }
                        }
                        translated = result;
                    }
                    else
                    {
                        translated = await client.TranslateBatchAsync(texts, cfg.Yandex.DefaultTargetLang, cfg.Yandex.DefaultSourceLang, glossary, false, cancellationToken).ConfigureAwait(false);
                    }
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
            if (gtm != null && (context.GtmMode ?? cfg.GlobalTM.WritePolicy) != "readonly")
            {
                foreach (var i in toTranslateIdx)
                {
                    var orig = rows[i].OrigText ?? string.Empty;
                    var tr = rows[i].TranslatedText ?? string.Empty;
                    if (!string.IsNullOrWhiteSpace(orig) && !string.IsNullOrWhiteSpace(tr))
                        gtm.Append(orig, tr, cfg.Yandex.DefaultSourceLang, cfg.Yandex.DefaultTargetLang, 1.0, false);
                }
            }
            cache?.Save();

            _tableIo.WriteRows(outputTable, delim, rows);
            if (context.OptHumanLoop || cfg.Optimization.HumanLoop)
            {
                var reviewPath = Path.Combine(Path.GetDirectoryName(outputTable) ?? Environment.CurrentDirectory, "review.tsv");
                var items = rows.Select(r => (r.OrigText ?? string.Empty, r.TranslatedText ?? string.Empty));
                new LocoTool.Core.Services.HumanLoopService().ExportReview(reviewPath, items);
            }
            if (gtm != null)
                Console.WriteLine($"[gtm] hits: {gtmHits}");
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
