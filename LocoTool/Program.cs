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
        Console.WriteLine();
        Console.WriteLine("Directory modes:");
        Console.WriteLine("  extract: если указан каталог как <out_dir> и --parser hashplus — создаёт множество TSV по секциям в каталоге");
        Console.WriteLine("  translate: если <in_dir> — переводит все *.tsv и пишет в <out_dir>");
        Console.WriteLine("  apply: если <dir> — склеивает все *.tsv внутри (header+строки); если <out_dir> — пишет выходной файл внутрь");
    }
}

