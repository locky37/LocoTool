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

            _tableIo.WriteRows(outputTable, delim, rows);
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
