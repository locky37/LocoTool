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
        string tableText = ReadTablePossiblyDirectory(tablePath);
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

            // If output looks like directory intention, create and write inferred name
            if (LooksLikeDirectory(outputPath))
            {
                Directory.CreateDirectory(outputPath);
                var name = Path.GetFileName(inputPath);
                var outPath = Path.Combine(outputPath, name);
                File.WriteAllText(outPath, output, Encoding.UTF8);
                Console.WriteLine($"[apply] OK -> {outPath}");
            }
            else
            {
                File.WriteAllText(outputPath, output, Encoding.UTF8);
                Console.WriteLine($"[apply] OK -> {outputPath}");
            }
            // Human-in-loop GTM learn: import review.tsv (final_text) and append to GTM
            try
            {
                var gtmEnabled = context.GtmEnabledOverride ?? cfg.GlobalTM.Enabled;
                if (gtmEnabled && (context.GtmLearn || context.OptHumanLoop))
                {
                    var dir = Directory.Exists(tablePath) ? tablePath : Path.GetDirectoryName(tablePath) ?? Environment.CurrentDirectory;
                    var review = Path.Combine(dir, "review.tsv");
                    if (File.Exists(review))
                    {
                        var gtm = new LocoTool.Core.Services.JsonlGlobalTranslationMemory(cfg.GlobalTM.RootPath, cfg.GlobalTM.Namespace, cfg.GlobalTM.PreferHumanEdited);
                        using var sr = new StreamReader(review, Encoding.UTF8);
                        var header = (sr.ReadLine() ?? string.Empty).Split('\t');
                        int iOrig = Array.IndexOf(header, "orig_text");
                        int iFinal = Array.IndexOf(header, "final_text");
                        int imported = 0;
                        string? line;
                        while ((line = sr.ReadLine()) != null)
                        {
                            var c = line.Split('\t');
                            var orig = iOrig >= 0 && iOrig < c.Length ? c[iOrig] : null;
                            var fin = iFinal >= 0 && iFinal < c.Length ? c[iFinal] : null;
                            if (!string.IsNullOrWhiteSpace(orig) && !string.IsNullOrWhiteSpace(fin))
                            {
                                gtm.Append(orig!, fin!, cfg.Yandex.DefaultSourceLang, cfg.Yandex.DefaultTargetLang, 1.0, true);
                                imported++;
                            }
                        }
                        Console.WriteLine($"[gtm] imported {imported} final_text rows from review.tsv");
                    }
                }
            }
            catch { }
            return Task.FromResult(0);
        }
        catch (Exception ex)
        {
            Console.WriteLine(ex.Message);
            return Task.FromResult(1);
        }
    }

    private static string ReadTablePossiblyDirectory(string path)
    {
        // If a single file is selected, try to expand to batch by prefix (prefix+*.tsv)
        if (!Directory.Exists(path))
        {
            if (File.Exists(path))
            {
                var (dir, filesList) = ResolveBatchBySelectedFile(path);
                return MergeFiles(filesList);
            }
            return File.ReadAllText(path, Encoding.UTF8);
        }

        var files = Directory.EnumerateFiles(path, "*.tsv").OrderBy(f => f, StringComparer.OrdinalIgnoreCase);
        return MergeFiles(files);
    }

    private static string MergeFiles(IEnumerable<string> files)
    {
        var sb = new StringBuilder();
        string? header = null;
        foreach (var f in files)
        {
            using var sr = new StreamReader(f, Encoding.UTF8);
            var hdr = sr.ReadLine() ?? string.Empty;
            header ??= hdr; // take header from first file
            string? line;
            while ((line = sr.ReadLine()) != null)
            {
                if (string.IsNullOrWhiteSpace(line)) continue;
                sb.AppendLine(line);
            }
        }
        if (header is null) return string.Empty;
        return header + Environment.NewLine + sb.ToString().TrimEnd();
    }

    private static bool LooksLikeDirectory(string path)
    {
        if (string.IsNullOrWhiteSpace(path)) return false;
        if (Directory.Exists(path)) return true;
        return string.IsNullOrEmpty(Path.GetExtension(path));
    }

    private static (string dir, List<string> files) ResolveBatchBySelectedFile(string selectedPath)
    {
        var dir = Path.GetDirectoryName(selectedPath) ?? Environment.CurrentDirectory;
        var name = Path.GetFileName(selectedPath);
        var baseName = Path.GetFileNameWithoutExtension(name);
        var plusIdx = baseName.IndexOf('+');
        if (plusIdx > 0)
        {
            var prefix = baseName[..plusIdx];
            var all = Directory.EnumerateFiles(dir, prefix + "+*.tsv", SearchOption.TopDirectoryOnly).OrderBy(f => f, StringComparer.OrdinalIgnoreCase).ToList();
            if (all.Count > 0) return (dir, all);
        }
        return (dir, new List<string> { selectedPath });
    }
}
