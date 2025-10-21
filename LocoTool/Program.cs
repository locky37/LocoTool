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
        Console.WriteLine("LocTool - extract / translate / apply / all / stats / convert");
        Console.WriteLine();
        Console.WriteLine("Usage:");
        Console.WriteLine("  LocTool extract   <input.txt>                                   <strings.hash|out_dir>          [options]");
        Console.WriteLine("  LocTool translate <strings_in.hash|in_dir|anchor_file>          <strings_out.hash|out_dir>      [options]");
        Console.WriteLine("  LocTool apply     <input.txt>           <strings.hash|dir|anchor_file> <output.txt|out_dir>      [options]");
        Console.WriteLine("  LocTool all       <input.txt>           <output.txt>                                         [options]");
        Console.WriteLine("  LocTool stats     <strings.hash|csv|tsv|dir>                                                [options]");
        Console.WriteLine("  LocTool convert   <in.tsv|in.hash> <out.hash>");
        Console.WriteLine();

        Console.WriteLine("Global options:");
        Console.WriteLine("  --config <path>                 Путь к config.json");
        Console.WriteLine("  --delimiter \"#\"|\"\\t\"|\",\"      Разделитель (по умолчанию \"#\")");
        Console.WriteLine("  --parser <name>                 Имя парсера (hash, hashplus, json, xliff)");
        Console.WriteLine("  --legacy-tsv                    Совместимость: TSV (\\t) вместо HSV (#)");
        Console.WriteLine();

        Console.WriteLine("extract options:");
        Console.WriteLine("  --dedup                         Экспорт только уникальных строк + dedup_map(.hash/.tsv)");
        Console.WriteLine("  --tm <path>                     Локальная TM (cache.json) — инициализируется при отсутствии");
        Console.WriteLine();

        Console.WriteLine("translate options:");
        Console.WriteLine("  --glossary <path>               Путь к глоссарию (glossary.json)");
        Console.WriteLine("  --price, --price-per-million N  Цена за 1 млн символов (для оценки)");
        Console.WriteLine("  --dedup                         Печать статистики дедупликации");
        Console.WriteLine("  --tm <path>                     Локальная TM (cache.json)");
        Console.WriteLine("  --batch-cache                   Кэширование результатов батчей перевода");
        Console.WriteLine("  --placeholders                  Маскирование плейсхолдеров (числа/даты/GUID/{}-блоки)");
        Console.WriteLine("  --hl-review                     Экспорт review (orig_text, mt_suggest, final_text)");
        Console.WriteLine("  --global-tm on|off              Включить/выключить Global TM");
        Console.WriteLine("  --tm-mode append|merge|readonly Режим записи GTM");
        Console.WriteLine("  --tm-namespace <name>           Пространство имён GTM");
        Console.WriteLine("  --tm-import <file>              Импорт в GTM (json|jsonl|tsv)");
        Console.WriteLine("  --tm-export <file>              Экспорт из GTM (jsonl|tsv)");
        Console.WriteLine("  --tm-learn                      Дообучать GTM из перевода");
        Console.WriteLine("  --tm-priority global|local      Порядок lookup: сначала GTM или local TM");
        Console.WriteLine();

        Console.WriteLine("apply options:");
        Console.WriteLine("  --apply-empty                   Разрешить применение пустых переводов");
        Console.WriteLine("  --hl-review                     Импорт правок из review и добавление в GTM");
        Console.WriteLine();

        Console.WriteLine("all options:");
        Console.WriteLine("  --glossary <path>               Глоссарий; --price <N>; опции translate применимы");
        Console.WriteLine();

        Console.WriteLine("stats options:");
        Console.WriteLine("  --price, --price-per-million N  Оценка стоимости");
        Console.WriteLine("  Поддерживает файл или каталог: каталог суммирует по *.hash/*.tsv/*.csv");
        Console.WriteLine();

        Console.WriteLine("Режимы каталогов и якорей:");
        Console.WriteLine("  extract:   <out_dir> + --parser hashplus — сохраняет много файлов по секциям");
        Console.WriteLine("  translate: <in_dir> → <out_dir> — все *.hash; якорь <in_dir/prefix+tag.hash> → prefix+*.hash");
        Console.WriteLine("  apply:     принимает каталог *.hash (склейка) или якорь prefix+tag.hash — расширяет по dedup_map");
        Console.WriteLine();

        Console.WriteLine("Примеры:");
        Console.WriteLine("  LocTool extract input.txt out --parser hashplus --dedup --delimiter #");
        Console.WriteLine("  LocTool translate out/input+randomname.hash translated --tm cache.json --global-tm on");
        Console.WriteLine("  LocTool apply input.txt translated/input+randomname.hash output.txt --parser hashplus");
        Console.WriteLine("  LocTool stats out --price 250                      (каталог)");
        Console.WriteLine("  LocTool stats out/strings.hash --price 250         (файл)");
    }
}