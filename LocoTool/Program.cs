using LocoTool.Cli;

namespace LocoTool;

/// <summary>
/// Application entry: parses CLI args, builds dependencies, routes to command.
/// </summary>
internal static class Program
{
    public static async Task<int> Main(string[] args)
    {
        if (args.Length == 0 || IsHelp(args[0]))
        {
            PrintHelp();
            return 0;
        }

        var context = CommandContext.FromArgs(args);
        var root = CompositionRoot.Build();
        var router = root.Router;

        try
        {
            using var cts = new CancellationTokenSource();
            Console.CancelKeyPress += (_, e) => { e.Cancel = true; cts.Cancel(); };
            return await router.RunAsync(context, cts.Token).ConfigureAwait(false);
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

    private static bool IsHelp(string s) =>
        s.Equals("-h", StringComparison.OrdinalIgnoreCase) ||
        s.Equals("--help", StringComparison.OrdinalIgnoreCase) ||
        s.Equals("help", StringComparison.OrdinalIgnoreCase);

    private static void PrintHelp()
    {
        Console.WriteLine("LocTool - extract / translate / apply / all / stats");
        Console.WriteLine();
        Console.WriteLine("Usage:");
        Console.WriteLine("  LocTool extract <input.txt> <strings.tsv|out_dir> [--config path.json] [--parser hash|hashplus|...] [--delimiter \"#\"|\"\\t\"|\",\"]");
        Console.WriteLine("  LocTool translate <strings_in.tsv|in_dir> <strings_out.tsv|out_dir> [--config path.json] [--glossary path.json] [--delimiter ...] [--price <perM>]");
        Console.WriteLine("  LocTool apply <input.txt> <strings.tsv|dir> <output.txt|out_dir> [--apply-empty] [--config path.json] [--delimiter ...]");
        Console.WriteLine("  LocTool all <input.txt> <output.txt> [--config path.json] [--glossary path.json] [--delimiter ...] [--price <perM>]");
        Console.WriteLine("  LocTool stats <strings.tsv|csv|hash> [--config path.json] [--delimiter ...] [--price <perM>]");
        Console.WriteLine();
        Console.WriteLine("Options:");
        Console.WriteLine("  --parser <name>   Имя парсера (hash, hashplus, json, xliff). Если не задан — автоопределение.");
        Console.WriteLine("  --price, --price-per-million   Цена за 1 млн символов (напр. 250.00)");
        Console.WriteLine("  --dedup           Удаление дубликатов сегментов перед переводом");
        Console.WriteLine("  --tm <path>       Локальная Translation Memory (cache.json)");
        Console.WriteLine("  --batch-cache     Кэширование результатов батчей перевода");
        Console.WriteLine("  --placeholders    Маскирование плейсхолдеров (числа) перед переводом");
        Console.WriteLine("  --hl-review       Экспорт review.tsv (orig_text, mt_suggest, final_text)");
        Console.WriteLine("  --global-tm on|off          Включить/выключить Global TM, перекрывая конфиг");
        Console.WriteLine("  --tm-mode append|merge|readonly   Режим записи в GTM");
        Console.WriteLine("  --tm-namespace <name>       Пространство имён GTM");
        Console.WriteLine("  --tm-import <file>          Импорт в GTM (json|jsonl|tsv)");
        Console.WriteLine("  --tm-export <file>          Экспорт из GTM (jsonl|tsv)");
        Console.WriteLine("  --tm-learn                  Дообучать GTM из текущего перевода");
        Console.WriteLine("  --tm-priority global|local  Порядок lookup: сначала GTM или local TM");
        Console.WriteLine();
        Console.WriteLine("Directory modes:");
        Console.WriteLine("  extract: если указан каталог как <out_dir> и --parser hashplus — создаёт множество TSV по секциям в каталоге");
        Console.WriteLine("  translate: если <in_dir> — переводит все *.tsv и пишет в <out_dir>");
        Console.WriteLine("  apply: если <dir> — склеивает все *.tsv внутри (header+строки); если <out_dir> — пишет выходной файл внутрь");
        Console.WriteLine();
        Console.WriteLine("Файл как якорь для каталога (после extract в каталог):");
        Console.WriteLine("  translate: LocTool translate out_dir/input+randomname.tsv translated_dir");
        Console.WriteLine("    — переведёт все файлы out_dir/input+*.tsv и сложит в translated_dir");
        Console.WriteLine("  apply:     LocTool apply input.txt translated_dir/input+randomname.tsv output.txt --parser hashplus");
        Console.WriteLine("    — применит склейку всех translated_dir/input+*.tsv и запишет в output.txt");
    }
}

