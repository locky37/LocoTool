// Program.cs
using CSnakes.Runtime;
using LocoTool.Config;
using LocoTool.Service;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Net.Http;
using System.Text;
using System.Text.Json;
// CSnakes: доступ к Python-модулю py/loctool.py
//using LocTool.py;

// Наши вспомогательные классы (из других файлов проекта)
//using static RestTranslateClient; // для DTO-шек если нужно

namespace LocoTool;

class Program
{
    static async Task<int> Main(string[] args)
    {

        var builder = Host.CreateApplicationBuilder(args);
        var home = Path.Join(Environment.CurrentDirectory, ".");

        builder.Services
            .WithPython()
            .WithHome(home)
            .FromRedistributable(); // Downloads Python automatically

        var app = builder.Build();
        var env = app.Services.GetRequiredService<IPythonEnvironment>();

        // Get the Python module
        var loctool = env.Loctool();


        if (args.Length == 0 || IsHelp(args[0]))
        {
            PrintHelp();
            return 0;
        }

        // CLI опции
        string? configPath = GetOptionValue(args, "--config");
        string? glossaryPathOverride = GetOptionValue(args, "--glossary");
        bool applyEmpty = args.Any(a => a.Equals("--apply-empty", StringComparison.OrdinalIgnoreCase));

        AppConfig config;
        try
        {
            config = AppConfig.Load(configPath);
        }
        catch (Exception ex)
        {
            Console.WriteLine($"[Config] {ex.Message}");
            return 1;
        }

        // Вход/выход по умолчанию из конфига
        string defaultInput = config.Files.DefaultInput;
        string defaultOutput = config.Files.DefaultOutput;

        var cmd = args[0].ToLowerInvariant();

        try
        {
            switch (cmd)
            {
                case "extract":
                    {
                        // LocTool extract <input.txt> <strings.tsv>
                        string inputPath = args.Length > 1 ? args[1] : defaultInput;
                        string tableOut = args.Length > 2 ? args[2] : "strings.tsv";

                        string input = File.ReadAllText(inputPath, Encoding.UTF8);
                        char delim = ResolveDelimiter(args, defaultDelim: '#');

                        string table = loctool.ExtractStrings(input, delim.ToString());
                        File.WriteAllText(tableOut, table, Encoding.UTF8);
                        Console.WriteLine($"[extract] OK -> {tableOut}");
                        return 0;
                    }

                case "translate":
                    {
                        // LocTool translate <strings_in.tsv|csv> <strings_out.tsv|csv> [--glossary path.json] [--config path]
                        string tableIn = args.Length > 1 ? args[1] : "strings.tsv";
                        string tableOut = args.Length > 2 ? args[2] : "strings_out.tsv";

                        var glossary = GlossaryLoader.Load(glossaryPathOverride ?? config.Yandex.GlossaryPath);
                        glossary = EnforceGlossaryLimit(glossary, config.Limits.MaxGlossaryPairs);

                        using var http = new HttpClient();
                        var client = new RestTranslateClient(http, config.Yandex.AuthHeader, config.Yandex.FolderId);

                        await TranslateTableAsync(
                            client, tableIn, tableOut, glossary,
                            config.Yandex.DefaultTargetLang, config.Yandex.DefaultSourceLang,
                            config.Limits.MaxCharsPerRequest
                        );

                        Console.WriteLine($"[translate] OK -> {tableOut}");
                        return 0;
                    }

                case "apply":
                    {
                        // LocTool apply <input.txt> <strings.tsv|csv> <output.txt> [--apply-empty] [--config path]
                        string inputPath = args.Length > 1 ? args[1] : defaultInput;
                        string tablePath = args.Length > 2 ? args[2] : "strings.tsv";
                        string outputPath = args.Length > 3 ? args[3] : defaultOutput;

                        string input = File.ReadAllText(inputPath, Encoding.UTF8);
                        string tableText = File.ReadAllText(tablePath, Encoding.UTF8);

                        string outputText = loctool.ApplyTranslations(input, tableText, applyEmpty);
                        File.WriteAllText(outputPath, outputText, Encoding.UTF8);

                        Console.WriteLine($"[apply] OK -> {outputPath}");
                        return 0;
                    }

                case "all":
                    {
                        // LocTool all <input.txt> <output.txt> [--glossary path.json] [--config path]
                        string inputPath = args.Length > 1 ? args[1] : defaultInput;
                        string outputPath = args.Length > 2 ? args[2] : defaultOutput;

                        string input = File.ReadAllText(inputPath, Encoding.UTF8);

                        // 1) extract
                        string tsv = loctool.ExtractStrings(input);

                        // 2) translate (in-memory)
                        var glossary = GlossaryLoader.Load(glossaryPathOverride ?? config.Yandex.GlossaryPath);
                        glossary = EnforceGlossaryLimit(glossary, config.Limits.MaxGlossaryPairs);

                        using var http = new HttpClient();
                        var client = new RestTranslateClient(http, config.Yandex.AuthHeader, config.Yandex.FolderId);

                        string tsvTranslated = await TranslateTsvInMemory(
                            client, tsv, glossary,
                            config.Yandex.DefaultTargetLang, config.Yandex.DefaultSourceLang,
                            config.Limits.MaxCharsPerRequest
                        );

                        // 3) apply
                        string outputText = loctool.ApplyTranslations(input, tsvTranslated, applyEmpty: false);
                        File.WriteAllText(outputPath, outputText, Encoding.UTF8);

                        Console.WriteLine($"[all] OK -> {outputPath}");
                        return 0;
                    }

                default:
                    PrintHelp();
                    return 1;
            }
        }
        catch (Exception ex)
        {
            Console.WriteLine($"[ERROR] {ex.Message}");
#if DEBUG
            Console.WriteLine(ex);
#endif
            return 1;
        }
    }

    // ===================== Перевод таблицы файлово =====================

    static async Task TranslateTableAsync(
        RestTranslateClient client,
        string tableInPath,
        string tableOutPath,
        (string src, string dst, bool exact)[] glossary,
        string targetLang,
        string? sourceLang,
        int maxCharsPerRequest)
    {
        //char delim = DetectDelimiter(File.ReadLines(tableInPath).FirstOrDefault() ?? "");
        char delim = ResolveDelimiter(Environment.GetCommandLineArgs(), defaultDelim: '#');
        var rows = ReadRows(tableInPath, delim).ToList();

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
            var translated = await client.TranslateBatchAsync(
                texts, targetLang, sourceLang, glossary, speller: false
            );

            for (int j = 0; j < batch.Count; j++)
                rows[batch[j]] = rows[batch[j]] with { TranslatedText = translated[j] };

            Console.WriteLine($"  [translate] batch {batch.Count} strings, chars: {sum}");
            batch.Clear(); sum = 0;
        }

        foreach (var idx in toTranslateIdx)
        {
            var add = rows[idx].OrigText?.Length ?? 0;
            if (sum + add > maxCharsPerRequest)
                await FlushAsync();

            batch.Add(idx);
            sum += add;
        }
        await FlushAsync();

        WriteRows(tableOutPath, delim, rows);
    }

    // ============== Перевод TSV в памяти (для режима all) ==============

    static async Task<string> TranslateTsvInMemory(
        RestTranslateClient client,
        string tsv,
        (string src, string dst, bool exact)[] glossary,
        string targetLang,
        string? sourceLang,
        int maxCharsPerRequest)
    {
        var lines = tsv.Split('\n');
        if (lines.Length == 0) return tsv;

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
        rows.Add(header); // keep header

        // Соберём индексы переводимых строк
        var data = new List<(int idx, string line, string[] cells)>();
        for (int i = 1; i < lines.Length; i++)
        {
            var l = lines[i].TrimEnd('\r');
            if (string.IsNullOrWhiteSpace(l)) { rows.Add(l); continue; }
            var c = l.Split('\t');
            data.Add((i, l, c));
        }

        var transIndices = data
            .Where(x => x.cells.Length > idxOrig &&
                        !string.IsNullOrWhiteSpace(x.cells[idxOrig]) &&
                        (x.cells.Length <= idxTrans || string.IsNullOrWhiteSpace(x.cells[idxTrans])))
            .Select(x => x.idx - 1) // индекс в data (сдвиг на header)
            .ToList();

        // Относительные индексы data
        var batch = new List<int>();
        int sum = 0;

        async System.Threading.Tasks.Task FlushAsync()
        {
            if (batch.Count == 0) return;
            var texts = batch.Select(ix => data[ix].cells[idxOrig]).ToArray();

            var translated = await client.TranslateBatchAsync(
                texts, targetLang, sourceLang, glossary, speller: false
            );

            for (int k = 0; k < batch.Count; k++)
            {
                var ix = batch[k];
                var cells = data[ix].cells;
                // расширим подстраховочно массив, если translated_text не существует
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
            Console.WriteLine($"  [translate] batch {batch.Count} strings, chars≈{sum}");
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

        // Сборка итогового TSV
        rows.AddRange(data.Select(d => d.line));
        return string.Join(Environment.NewLine, rows);
    }

    // ====================== Утилиты CSV/TSV и глоссарий ======================

    static char DetectDelimiter(string header) => header.Contains('\t') ? '\t' : ',';

    record Row(int OriginalLineNo, int FieldIndex, string OrigText, string TranslatedText);

    static IEnumerable<Row> ReadRows(string path, char delim)
    {
        using var sr = new StreamReader(path, Encoding.UTF8);

        // читаем заголовок и определяем индексы нужных колонок
        var headerLine = (sr.ReadLine() ?? "").TrimEnd('\r');
        var cols = headerLine.Split(delim);

        int iLine = Array.FindIndex(cols, c => c.Equals("original_line_no", StringComparison.OrdinalIgnoreCase));
        int iField = Array.FindIndex(cols, c => c.Equals("field_index", StringComparison.OrdinalIgnoreCase));
        int iOrig = Array.FindIndex(cols, c => c.Equals("orig_text", StringComparison.OrdinalIgnoreCase));
        int iTrans = Array.FindIndex(cols, c => c.Equals("translated_text", StringComparison.OrdinalIgnoreCase));

        if (iLine < 0 || iField < 0 || iOrig < 0 || iTrans < 0)
            throw new InvalidOperationException(
                $"В таблице должны быть колонки: original_line_no, field_index, orig_text, translated_text. " +
                $"Найдено: {string.Join(", ", cols)}");

        string? line;
        while ((line = sr.ReadLine()) != null)
        {
            var c = line.TrimEnd('\r').Split(delim);

            // безопасно вытаскиваем ячейки (могут быть пустые / отсутствовать в конце)
            string Get(int idx) => (idx >= 0 && idx < c.Length) ? c[idx] : "";

            // парсим числа с инвариантной культурой
            if (!int.TryParse(Get(iLine), NumberStyles.Integer, CultureInfo.InvariantCulture, out var lineNo)) continue;
            if (!int.TryParse(Get(iField), NumberStyles.Integer, CultureInfo.InvariantCulture, out var fieldIdx)) continue;

            yield return new Row(
                lineNo,
                fieldIdx,
                Get(iOrig),
                Get(iTrans)
            );
        }
    }

    static void WriteRows(string path, char delim, IEnumerable<Row> rows)
    {
        using var sw = new StreamWriter(path, false, Encoding.UTF8);
        sw.WriteLine(string.Join(delim, new[] { "original_line_no", "field_index", "record_id_guess", "orig_text", "translated_text" }));
        foreach (var r in rows)
        {
            sw.WriteLine(string.Join(delim, new[]
            {
                r.OriginalLineNo.ToString(CultureInfo.InvariantCulture),
                r.FieldIndex.ToString(CultureInfo.InvariantCulture),
                "", // record_id_guess — не обязателен, можно доработать
                (r.OrigText ?? "").Replace("\n"," "),
                (r.TranslatedText ?? "").Replace("\n"," ")
            }));
        }
    }

    static (string src, string dst, bool exact)[] EnforceGlossaryLimit((string src, string dst, bool exact)[] pairs, int maxPairs)
    {
        if (pairs == null || pairs.Length == 0) return Array.Empty<(string, string, bool)>();
        if (pairs.Length <= maxPairs) return pairs;
        Console.WriteLine($"[glossary] Too many pairs: {pairs.Length} > {maxPairs}. Truncating.");
        return pairs.Take(maxPairs).ToArray();
    }

    // ====================== CLI helpers ======================

    static bool IsHelp(string s) =>
        s.Equals("-h", StringComparison.OrdinalIgnoreCase) ||
        s.Equals("--help", StringComparison.OrdinalIgnoreCase) ||
        s.Equals("help", StringComparison.OrdinalIgnoreCase);

    static string? GetOptionValue(string[] args, string key)
    {
        var i = Array.FindIndex(args, a => a.Equals(key, StringComparison.OrdinalIgnoreCase));
        if (i >= 0 && i + 1 < args.Length)
            return args[i + 1];
        return null;
    }

    static char ResolveDelimiter(string[] args, char defaultDelim = '#')
    {
        var s = GetOptionValue(args, "--delimiter");   // например: "--delimiter" "#"
        if (string.IsNullOrEmpty(s))
            return defaultDelim;
        return s switch
        {
            "\\t" => '\t',
            "tab" => '\t',
            "#" => '#',
            "," => ',',
            ";" => ';',
            "|" => '|',
            _ when s.Length == 1 => s[0],
            _ => defaultDelim
        };
    }

    static void PrintHelp()
    {
        Console.WriteLine("LocoTool — extract / translate / apply / all");
        Console.WriteLine();
        Console.WriteLine("Usage:");
        Console.WriteLine("  LocoTool extract <input.txt> <strings.tsv> [--config path.json]");
        Console.WriteLine("  LocoTool translate <strings_in.tsv|csv> <strings_out.tsv|csv> [--glossary path.json] [--config path.json]");
        Console.WriteLine("  LocoTool apply <input.txt> <strings.tsv|csv> <output.txt> [--apply-empty] [--config path.json]");
        Console.WriteLine("  LocoTool all <input.txt> <output.txt> [--glossary path.json] [--config path.json]");
        Console.WriteLine();
        Console.WriteLine("Default config keys (config.json):");
        Console.WriteLine("  Yandex.ApiKey / Yandex.FolderId / Yandex.DefaultSourceLang / Yandex.DefaultTargetLang / Yandex.GlossaryPath");
        Console.WriteLine("  Limits.MaxCharsPerRequest / Limits.MaxGlossaryPairs");
        Console.WriteLine("  Files.DefaultInput / Files.DefaultOutput");
    }
}