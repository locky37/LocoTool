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
        Console.WriteLine("  LocTool extract <input.txt> <strings.tsv> [--config path.json] [--delimiter \"#\"|\"\\t\"|\",\"]");
        Console.WriteLine("  LocTool translate <strings_in.tsv|csv|hash> <strings_out.tsv|csv|hash> [--config path.json] [--glossary path.json] [--delimiter ...] [--price <perM>]");
        Console.WriteLine("  LocTool apply <input.txt> <strings.tsv|csv|hash> <output.txt> [--apply-empty] [--config path.json] [--delimiter ...]");
        Console.WriteLine("  LocTool all <input.txt> <output.txt> [--config path.json] [--glossary path.json] [--delimiter ...] [--price <perM>]");
        Console.WriteLine("  LocTool stats <strings.tsv|csv|hash> [--config path.json] [--delimiter ...] [--price <perM>]");
        Console.WriteLine();
        Console.WriteLine("Options:");
        Console.WriteLine("  --parser <name>   Имя парсера (пример: hash, json, xliff). Если не задан — автоопределение.");
        Console.WriteLine("  --price, --price-per-million   Цена за 1 млн символов (напр. 250.00)");
    }
}

