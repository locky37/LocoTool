using System.Text;
using LocoTool.Config;
using LocoTool.Core.Abstractions;

namespace LocoTool.Cli;

/// <summary>'all' command: extract -> translate -> apply, in-memory.
/// Preserves legacy flow and messaging.</summary>
public sealed class AllCommand : ICommandRunner
{
    private readonly IConfigService _config;
    private readonly IParsingService _parsing;
    private readonly IGlossaryService _glossary;
    private readonly ITableIo _tableIo;
    private readonly IStatsService _stats;

    public AllCommand(IConfigService config, IParsingService parsing, IGlossaryService glossary, ITranslateClient _ignored, ITableIo tableIo, IStatsService stats)
    { _config = config; _parsing = parsing; _glossary = glossary; _tableIo = tableIo; _stats = stats; }

    public string Name => "all";

    public async Task<int> RunAsync(CommandContext context, CancellationToken cancellationToken)
    {
        var cfgRes = _config.Load(context.ConfigPath);
        if (!cfgRes.Success || cfgRes.Value is null)
        { Console.WriteLine($"[Config] {cfgRes.Error}"); return 1; }
        var cfg = cfgRes.Value;

        string inputPath = context.Args.Length > 1 ? context.Args[1] : cfg.Files.DefaultInput;
        string outputPath = context.Args.Length > 2 ? context.Args[2] : cfg.Files.DefaultOutput;
        string input = File.ReadAllText(inputPath, Encoding.UTF8);
        string? sample = File.Exists(inputPath) ? File.ReadLines(inputPath).FirstOrDefault() : null;

        // 1) extract
        string tsv = _parsing.Extract(input, '\t', false, context.ParserName ?? cfg.Parsers.Default, inputPath, sample, cfg.Parsers.Folder, cfg.Parsers.Assemblies);

        // stats print
        var (totalChars, stringsCount) = ComputeStatsFromTsvText(tsv);
        var (batches, exactCost, paddedCost, paddedChars) = _stats.EstimateCost(totalChars, cfg.Limits.MaxCharsPerRequest, context.PricePerMillion);
        Console.WriteLine($"[stats] строк к переводу: {stringsCount}");
        Console.WriteLine($"[stats] всего символов: {totalChars:N0}");
        Console.WriteLine($"[stats] пачек по {cfg.Limits.MaxCharsPerRequest} симв.: {batches:N0}");
        if (exactCost is not null && paddedCost is not null)
        {
            Console.WriteLine($"[stats] оценка (по символам): ~{exactCost:0.00}");
            Console.WriteLine($"[stats] оценка (по пачкам):  ~{paddedCost:0.00}  (учтено {paddedChars:N0} симв.)");
        }

        // glossary
        var glossary = _glossary.Load(context.GlossaryPath ?? cfg.Yandex.GlossaryPath);
        glossary = _glossary.EnforceLimit(glossary, cfg.Limits.MaxGlossaryPairs);

        // 2) translate in-memory tsv
        string tsvTranslated = await TranslateTsvInMemory(tsv, glossary, cfg.Yandex.DefaultTargetLang, cfg.Yandex.DefaultSourceLang, cfg.Limits.MaxCharsPerRequest, cfg.Yandex.AuthHeader, cfg.Yandex.FolderId, cancellationToken).ConfigureAwait(false);

        // 3) apply
        string outputText = _parsing.Apply(input, tsvTranslated, '\t', false, context.ParserName ?? cfg.Parsers.Default, inputPath, sample, cfg.Parsers.Folder, cfg.Parsers.Assemblies);
        File.WriteAllText(outputPath, outputText, Encoding.UTF8);
        Console.WriteLine($"[all] OK -> {outputPath}");
        return 0;

        // local helpers copied from legacy behavior
        async Task<string> TranslateTsvInMemory(string tsvText, (string src, string dst, bool exact)[] gloss, string targetLang, string? sourceLang, int maxCharsPerRequest, string authHeader, string? folderId, CancellationToken ct)
        {
            var lines = tsvText.Split('\n');
            if (lines.Length == 0) return tsvText;
            var header = lines[0].TrimEnd('\r');
            int idxLineNo = -1, idxFieldIdx = -1, idxOrig = -1, idxTrans = -1;
            var hdr = header.Split('\t');
            for (int i = 0; i < hdr.Length; i++)
            {
                switch (hdr[i])
                {
                    case "original_line_no": idxLineNo = i; break;
                    case "field_index": idxFieldIdx = i; break;
                    case "orig_text": idxOrig = i; break;
                    case "translated_text": idxTrans = i; break;
                }
            }
            var rows = new List<string>(lines.Length);
            rows.Add(header);
            var data = new List<(int idx, string line, string[] cells)>();
            for (int i = 1; i < lines.Length; i++)
            {
                var l = lines[i].TrimEnd('\r');
                if (string.IsNullOrWhiteSpace(l)) { rows.Add(l); continue; }
                var c = l.Split('\t');
                data.Add((i, l, c));
            }
            var transIndices = data
                .Where(x => x.cells.Length > idxOrig && !string.IsNullOrWhiteSpace(x.cells[idxOrig]) && (x.cells.Length <= idxTrans || string.IsNullOrWhiteSpace(x.cells[idxTrans])))
                .Select(x => x.idx - 1)
                .ToList();

            var batch = new List<int>();
            int sum = 0;
            var client = new LocoTool.Core.Services.RestTranslateClientAdapter(
                () => new LocoTool.Service.RestTranslateClient(new HttpClient(), authHeader, folderId));
            async Task FlushAsync()
            {
                if (batch.Count == 0) return;
                var texts = batch.Select(ix => data[ix].cells[idxOrig]).ToArray();
                var translated = await client.TranslateBatchAsync(texts, targetLang, sourceLang, gloss, false, ct).ConfigureAwait(false);
                for (int k = 0; k < batch.Count; k++)
                {
                    var ix = batch[k];
                    var cells = data[ix].cells;
                    if (cells.Length <= idxTrans)
                    {
                        var expanded = new string[idxTrans + 1];
                        Array.Copy(cells, expanded, cells.Length);
                        for (int z = cells.Length; z < expanded.Length; z++) expanded[z] = "";
                        cells = expanded;
                    }
                    cells[idxTrans] = (translated[k] ?? "").Replace("\n", " ");
                    data[ix] = (data[ix].idx, string.Join('\t', cells), cells);
                }
                Console.WriteLine($"  [translate] batch {batch.Count} strings, chars?{sum}");
                batch.Clear(); sum = 0;
            }
            foreach (var idx in transIndices)
            {
                var add = data[idx].cells[idxOrig]?.Length ?? 0;
                if (sum + add > maxCharsPerRequest)
                    await FlushAsync();
                batch.Add(idx);
                sum += add;
            }
            await FlushAsync();
            rows.AddRange(data.Select(d => d.line));
            return string.Join(Environment.NewLine, rows);
        }

        static (long totalChars, int stringsCount) ComputeStatsFromTsvText(string tsv)
        {
            var lines = tsv.Split('\n');
            if (lines.Length == 0) return (0, 0);
            var header = lines[0].TrimEnd('\r').Split('\t');
            int idxOrig = Array.FindIndex(header, h => h == "orig_text");
            int idxTrans = Array.FindIndex(header, h => h == "translated_text");
            long total = 0; int cnt = 0;
            for (int i = 1; i < lines.Length; i++)
            {
                var l = lines[i].TrimEnd('\r');
                if (string.IsNullOrWhiteSpace(l)) continue;
                var c = l.Split('\t');
                var orig = (idxOrig >= 0 && idxOrig < c.Length) ? c[idxOrig] : "";
                var tran = (idxTrans >= 0 && idxTrans < c.Length) ? c[idxTrans] : "";
                if (!string.IsNullOrWhiteSpace(orig) && string.IsNullOrWhiteSpace(tran)) { total += orig.Length; cnt++; }
            }
            return (total, cnt);
        }
    }
}
