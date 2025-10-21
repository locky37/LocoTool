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
            var effectiveParser = context.ParserName ?? cfg.Parsers.Default;
            var table = _parsing.Extract(
                input,
                context.Delimiter,
                context.ApplyEmpty,
                effectiveParser,
                inputPath,
                sample,
                cfg.Parsers.Folder,
                cfg.Parsers.Assemblies);
            var isHashPlus = string.Equals(effectiveParser, "hashplus", StringComparison.OrdinalIgnoreCase);
            var outLooksDir = LooksLikeDirectory(tableOut);
            (int total, int unique) dedupAllStats = (total: 0, unique: 0);
            var aggregatedMap = new List<string>();

            if (isHashPlus)
            {
                var groups = GroupByRecordId(table, context.Delimiter);
                if (outLooksDir)
                {
                    var dir = tableOut;
                    Directory.CreateDirectory(dir);
                    var prefix = Path.GetFileNameWithoutExtension(inputPath);
                    var ext = ".tsv";
                    foreach (var (tag, content) in groups)
                    {
                        var outPath = Path.Combine(dir, $"{prefix}+{tag}{ext}");
                        if (context.OptDedup || cfg.Optimization.Deduplicate)
                        {
                            var (uniqueContent, mapLines, stats) = BuildUniqueAndMap(content, context.Delimiter);
                            File.WriteAllText(outPath, uniqueContent, Encoding.UTF8);
                            dedupAllStats.total += stats.total; dedupAllStats.unique += stats.unique;
                            aggregatedMap.AddRange(mapLines.Select(l => $"{prefix}+{tag}\t" + l));
                            Console.WriteLine($"[dedup] Уникальных: {stats.unique} / Повторов: {stats.total - stats.unique} ({((stats.total - stats.unique)/(double)Math.Max(1,stats.total)):P0})");
                        }
                        else
                        {
                            File.WriteAllText(outPath, content, Encoding.UTF8);
                        }
                        Console.WriteLine($"[extract] OK -> {outPath}");
                    }
                    // Write aggregated map and stats in dir
                    if ((context.OptDedup || cfg.Optimization.Deduplicate) && aggregatedMap.Count > 0)
                    {
                        var mapPath = Path.Combine(dir, "dedup_map.tsv");
                        File.WriteAllLines(mapPath, new[] { "file\toriginal_line_no\tfield_index\trecord_id_guess\torig_text\tunique_index" }.Concat(aggregatedMap));
                        WriteDedupStatsJson(dir, dedupAllStats.total, dedupAllStats.unique);
                    }
                }
                else
                {
                    var dir = Path.GetDirectoryName(tableOut) ?? Environment.CurrentDirectory;
                    var baseName = Path.GetFileNameWithoutExtension(tableOut);
                    var ext = Path.GetExtension(tableOut);
                    if (string.IsNullOrEmpty(ext)) ext = ".tsv";
                    foreach (var (tag, content) in groups)
                    {
                        var outPath = Path.Combine(dir, $"{baseName}+{tag}{ext}");
                        if (context.OptDedup || cfg.Optimization.Deduplicate)
                        {
                            var (uniqueContent, mapLines, stats) = BuildUniqueAndMap(content, context.Delimiter);
                            File.WriteAllText(outPath, uniqueContent, Encoding.UTF8);
                            dedupAllStats.total += stats.total; dedupAllStats.unique += stats.unique;
                            aggregatedMap.AddRange(mapLines.Select(l => $"{baseName}+{tag}\t" + l));
                            Console.WriteLine($"[dedup] Уникальных: {stats.unique} / Повторов: {stats.total - stats.unique} ({((stats.total - stats.unique)/(double)Math.Max(1,stats.total)):P0})");
                        }
                        else
                        {
                            File.WriteAllText(outPath, content, Encoding.UTF8);
                        }
                        Console.WriteLine($"[extract] OK -> {outPath}");
                    }
                    if ((context.OptDedup || cfg.Optimization.Deduplicate) && aggregatedMap.Count > 0)
                    {
                        var mapPath = Path.Combine(dir, "dedup_map.tsv");
                        File.WriteAllLines(mapPath, new[] { "file\toriginal_line_no\tfield_index\trecord_id_guess\torig_text\tunique_index" }.Concat(aggregatedMap));
                        WriteDedupStatsJson(dir, dedupAllStats.total, dedupAllStats.unique);
                    }
                }
            }
            else
            {
                // Non-splitting parsers keep single-file behavior; if tableOut is a directory, use default name
                if (outLooksDir)
                {
                    Directory.CreateDirectory(tableOut);
                    var name = Path.GetFileNameWithoutExtension(inputPath);
                    var outPath = Path.Combine(tableOut, $"{name}.tsv");
                    if (context.OptDedup || cfg.Optimization.Deduplicate)
                    {
                        var (uniqueContent, mapLines, stats) = BuildUniqueAndMap(table, context.Delimiter);
                        File.WriteAllText(outPath, uniqueContent, Encoding.UTF8);
                        var mapPath = Path.Combine(tableOut, "dedup_map.tsv");
                        File.WriteAllLines(mapPath, new[] { "original_line_no\tfield_index\trecord_id_guess\torig_text\tunique_index" }.Concat(mapLines));
                        WriteDedupStatsJson(tableOut, stats.total, stats.unique);
                        Console.WriteLine($"[dedup] Уникальных: {stats.unique} / Повторов: {stats.total - stats.unique} ({((stats.total - stats.unique)/(double)Math.Max(1,stats.total)):P0})");
                    }
                    else
                    {
                        File.WriteAllText(outPath, table, Encoding.UTF8);
                    }
                    Console.WriteLine($"[extract] OK -> {outPath}");
                }
                else
                {
                    if (context.OptDedup || cfg.Optimization.Deduplicate)
                    {
                        var (uniqueContent, mapLines, stats) = BuildUniqueAndMap(table, context.Delimiter);
                        File.WriteAllText(tableOut, uniqueContent, Encoding.UTF8);
                        var dir = Path.GetDirectoryName(tableOut) ?? Environment.CurrentDirectory;
                        var mapPath = Path.Combine(dir, "dedup_map.tsv");
                        File.WriteAllLines(mapPath, new[] { "original_line_no\tfield_index\trecord_id_guess\torig_text\tunique_index" }.Concat(mapLines));
                        WriteDedupStatsJson(dir, stats.total, stats.unique);
                        Console.WriteLine($"[dedup] Уникальных: {stats.unique} / Повторов: {stats.total - stats.unique} ({((stats.total - stats.unique)/(double)Math.Max(1,stats.total)):P0})");
                    }
                    else
                    {
                        File.WriteAllText(tableOut, table, Encoding.UTF8);
                    }
                    Console.WriteLine($"[extract] OK -> {tableOut}");
                }
            }
            return Task.FromResult(0);
        }
        catch (Exception ex)
        {
            Console.WriteLine(ex.Message);
            return Task.FromResult(1);
        }
    }

    private static List<(string tag, string content)> GroupByRecordId(string tsv, char delim)
    {
        var lines = tsv.Split('\n');
        if (lines.Length == 0) return new();
        var header = lines[0].TrimEnd('\r');
        var cols = header.Split(delim);
        int iTag = Array.IndexOf(cols, "record_id_guess");
        if (iTag < 0) iTag = 2; // fallback to default column index

        var dict = new Dictionary<string, List<string>>(StringComparer.OrdinalIgnoreCase);
        for (int i = 1; i < lines.Length; i++)
        {
            var l = lines[i].TrimEnd('\r');
            if (string.IsNullOrWhiteSpace(l)) continue;
            var c = l.Split(delim);
            var tag = (iTag >= 0 && iTag < c.Length) ? c[iTag] : "_";
            if (!dict.TryGetValue(tag, out var list))
            {
                list = new List<string> { header };
                dict[tag] = list;
            }
            list.Add(l);
        }
        return dict.Select(kv => (kv.Key, string.Join(Environment.NewLine, kv.Value))).ToList();
    }

    private static bool LooksLikeDirectory(string path)
    {
        if (string.IsNullOrWhiteSpace(path)) return false;
        if (Directory.Exists(path)) return true;
        // Heuristic: no extension -> likely directory intention
        return string.IsNullOrEmpty(Path.GetExtension(path));
    }

    private static (string uniqueContent, List<string> mapLines, (int total, int unique) stats) BuildUniqueAndMap(string tsv, char delim)
    {
        var lines = tsv.Split('\n');
        if (lines.Length == 0) return (tsv, new List<string>(), (0,0));
        var header = lines[0].TrimEnd('\r');
        var hdr = header.Split(delim);
        int iLine = Array.IndexOf(hdr, "original_line_no");
        int iField = Array.IndexOf(hdr, "field_index");
        int iRec = Array.IndexOf(hdr, "record_id_guess");
        int iOrig = Array.IndexOf(hdr, "orig_text");

        var rows = new List<string[]>();
        for (int i = 1; i < lines.Length; i++)
        {
            var l = lines[i].TrimEnd('\r');
            if (string.IsNullOrWhiteSpace(l)) continue;
            rows.Add(l.Split(delim));
        }
        var candidates = rows.Where(r => iOrig >= 0 && iOrig < r.Length && !string.IsNullOrWhiteSpace(r[iOrig])).ToList();
        var texts = candidates.Select(r => r[iOrig] ?? string.Empty).ToList();

        var dedup = new LocoTool.Core.Services.Deduplicator();
        var (uniqueList, map) = dedup.Deduplicate(texts);

        // Build map lines and unique rows
        var firstIndexForUniq = new int[uniqueList.Count];
        Array.Fill(firstIndexForUniq, -1);
        var mapLines = new List<string>();
        for (int idx = 0; idx < candidates.Count; idx++)
        {
            var uniq = map[idx];
            if (firstIndexForUniq[uniq] == -1) firstIndexForUniq[uniq] = idx;
            var r = candidates[idx];
            string get(int ii) => ii >= 0 && ii < r.Length ? r[ii] : string.Empty;
            mapLines.Add(string.Join(delim, new[] { get(iLine), get(iField), get(iRec), get(iOrig), uniq.ToString() }));
        }

        var uniqueRows = new List<string> { header };
        for (int u = 0; u < uniqueList.Count; u++)
        {
            var r = candidates[firstIndexForUniq[u]];
            // ensure translated_text column exists; leave empty
            if (r.Length < hdr.Length) Array.Resize(ref r, hdr.Length);
            if (r.Length >= hdr.Length) r[hdr.Length - 1] = string.Empty;
            uniqueRows.Add(string.Join(delim, r));
        }
        var uniqueContent = string.Join(Environment.NewLine, uniqueRows);
        return (uniqueContent, mapLines, (texts.Count, uniqueList.Count));
    }

    private static void WriteDedupStatsJson(string dir, int total, int unique)
    {
        try
        {
            var duplicates = Math.Max(0, total - unique);
            var rate = total > 0 ? (duplicates / (double)total) : 0.0;
            var json = $"{{ \"total\": {total}, \"unique\": {unique}, \"duplicates\": {duplicates}, \"dedupRate\": {rate:0.##} }}";
            File.WriteAllText(Path.Combine(dir, "dedup_stats.json"), json);
        }
        catch { }
    }
}
