using CSnakes.Runtime;
using LocoTool.Config;
using LocoTool.Service;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using System.Globalization;
using System.Text;

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
        bool hasPrice = TryParsePricePerMillion(args, out var pricePerMillion);

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
                case "stats":
                    {
                        // LocTool stats <strings.tsv|csv|hash> [--delimiter ...] [--price ...]
                        string tableIn = args.Length > 1 ? args[1] : "strings.tsv";
                        char delim = ResolveDelimiter(args, defaultDelim: '#');
                        var (totalChars, stringsCount) = ComputeStatsFromTable(tableIn, delim);
                        PrintCostEstimation(totalChars, stringsCount, config.Limits.MaxCharsPerRequest, hasPrice ? pricePerMillion : null);
                        return 0;
                    }
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
                        // ... существующий код translate ...
                        string tableIn = args.Length > 1 ? args[1] : "strings.tsv";
                        string tableOut = args.Length > 2 ? args[2] : "strings_out.tsv";
                        char delim = ResolveDelimiter(args, defaultDelim: '#');

                        // ДО перевода — посчитаем и выведем оценку
                        var (totalChars, stringsCount) = ComputeStatsFromTable(tableIn, delim);
                        PrintCostEstimation(totalChars, stringsCount, config.Limits.MaxCharsPerRequest, hasPrice ? pricePerMillion : null);

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

                        char delim = ResolveDelimiter(args, defaultDelim: '#');

                        string input = File.ReadAllText(inputPath, Encoding.UTF8);
                        string tableText = File.ReadAllText(tablePath, Encoding.UTF8);

                        string outputText = loctool.ApplyTranslations(input, tableText, applyEmpty, delim.ToString());
                        File.WriteAllText(outputPath, outputText, Encoding.UTF8);

                        Console.WriteLine($"[apply] OK -> {outputPath}");
                        return 0;
                    }

                case "all":
                    {
                        // ... существующий код all ...
                        string inputPath = args.Length > 1 ? args[1] : defaultInput;
                        string outputPath = args.Length > 2 ? args[2] : defaultOutput;

                        string input = File.ReadAllText(inputPath, Encoding.UTF8);

                        // 1) extract (генерим TSV в памяти)
                        string tsv = loctool.ExtractStrings(input); // если делал параметр delimiter — прокинь тут ResolveDelimiter(...)

                        // ДО перевода — посчитаем и выведем оценку
                        var (totalChars, stringsCount) = ComputeStatsFromTsvText(tsv);
                        PrintCostEstimation(totalChars, stringsCount, config.Limits.MaxCharsPerRequest, hasPrice ? pricePerMillion : null);

                        var glossary = GlossaryLoader.Load(glossaryPathOverride ?? config.Yandex.GlossaryPath);
                        glossary = EnforceGlossaryLimit(glossary, config.Limits.MaxGlossaryPairs);

                        using var http = new HttpClient();
                        var client = new RestTranslateClient(http, config.Yandex.AuthHeader, config.Yandex.FolderId);

                        string tsvTranslated = await TranslateTsvInMemory(
                            client, tsv, glossary,
                            config.Yandex.DefaultTargetLang, config.Yandex.DefaultSourceLang,
                            config.Limits.MaxCharsPerRequest
                        );

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

    //static char DetectDelimiter(string header) => header.Contains('\t') ? '\t' : ',';

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

    static bool TryParsePricePerMillion(string[] args, out double pricePerMillion)
    {
        pricePerMillion = 0;
        var val = GetOptionValue(args, "--price") ?? GetOptionValue(args, "--price-per-million");
        if (string.IsNullOrWhiteSpace(val)) return false;
        // Допускаем "250", "250.5", "250,5"
        if (double.TryParse(val, NumberStyles.Float, CultureInfo.InvariantCulture, out pricePerMillion)) return true;
        if (double.TryParse(val, NumberStyles.Float, new CultureInfo("ru-RU"), out pricePerMillion)) return true;
        return false;
    }

    static (long totalChars, int stringsCount) ComputeStatsFromTable(string tablePath, char delim)
    {
        long total = 0;
        int cnt = 0;
        foreach (var r in ReadRows(tablePath, delim))
        {
            // учитываем ТОЛЬКО те строки, у которых есть исходный текст и пустой перевод
            if (!string.IsNullOrWhiteSpace(r.OrigText) && string.IsNullOrWhiteSpace(r.TranslatedText))
            {
                total += r.OrigText.Length;
                cnt++;
            }
        }
        return (total, cnt);
    }

    static (long totalChars, int stringsCount) ComputeStatsFromTsvText(string tsv)
    {
        var lines = tsv.Split('\n');
        if (lines.Length == 0) return (0, 0);

        var header = lines[0].TrimEnd('\r').Split('\t');
        int idxOrig = Array.FindIndex(header, h => h == "orig_text");
        int idxTrans = Array.FindIndex(header, h => h == "translated_text");

        long total = 0;
        int cnt = 0;

        for (int i = 1; i < lines.Length; i++)
        {
            var l = lines[i].TrimEnd('\r');
            if (string.IsNullOrWhiteSpace(l)) continue;
            var c = l.Split('\t');
            var orig = (idxOrig >= 0 && idxOrig < c.Length) ? c[idxOrig] : "";
            var tran = (idxTrans >= 0 && idxTrans < c.Length) ? c[idxTrans] : "";
            if (!string.IsNullOrWhiteSpace(orig) && string.IsNullOrWhiteSpace(tran))
            {
                total += orig.Length;
                cnt++;
            }
        }
        return (total, cnt);
    }

    static void PrintCostEstimation(long totalChars, int stringsCount, int maxCharsPerRequest, double? pricePerMillionOpt)
    {
        var batches = (int)Math.Ceiling(totalChars / (double)maxCharsPerRequest);
        Console.WriteLine($"[stats] Строк к переводу: {stringsCount}");
        Console.WriteLine($"[stats] Суммарно символов: {totalChars:N0}");
        Console.WriteLine($"[stats] Батчей по {maxCharsPerRequest} симв.: {batches:N0}");

        if (pricePerMillionOpt is double pricePerMillion)
        {
            var exactCost = (totalChars / 1_000_000.0) * pricePerMillion;
            var paddedChars = (long)batches * maxCharsPerRequest;
            var paddedCost = (paddedChars / 1_000_000.0) * pricePerMillion;

            Console.WriteLine($"[stats] Оценка стоимости (по символам): ~{exactCost:0.00}");
            Console.WriteLine($"[stats] Оценка с запасом (по батчам):  ~{paddedCost:0.00}  (учтено {paddedChars:N0} симв.)");
        }
    }

    static void PrintHelp()
    {
        Console.WriteLine("LocTool — extract / translate / apply / all / stats");
        Console.WriteLine();
        Console.WriteLine("Usage:");
        Console.WriteLine("  LocTool extract <input.txt> <strings.tsv> [--config path.json] [--delimiter \"#\"|\"\\t\"|\",\"]");
        Console.WriteLine("  LocTool translate <strings_in.tsv|csv|hash> <strings_out.tsv|csv|hash> [--config path.json] [--glossary path.json] [--delimiter ...] [--price <perM>]");
        Console.WriteLine("  LocTool apply <input.txt> <strings.tsv|csv|hash> <output.txt> [--apply-empty] [--config path.json] [--delimiter ...]");
        Console.WriteLine("  LocTool all <input.txt> <output.txt> [--config path.json] [--glossary path.json] [--delimiter ...] [--price <perM>]");
        Console.WriteLine("  LocTool stats <strings.tsv|csv|hash> [--config path.json] [--delimiter ...] [--price <perM>]");
        Console.WriteLine();
        Console.WriteLine("Options:");
        Console.WriteLine("  --price, --price-per-million   Цена за 1 млн символов (вкл. НДС), например 250.00");
    }
}