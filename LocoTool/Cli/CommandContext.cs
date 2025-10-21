using System.Globalization;
using LocoTool.Core.Services;

namespace LocoTool.Cli;

/// <summary>CLI context parsed from args, passed to commands.</summary>
public sealed class CommandContext
{
    public required string Command { get; init; }
    public string[] Args { get; init; } = Array.Empty<string>();

    public string? ConfigPath { get; init; }
    public string? GlossaryPath { get; init; }
    public string? ParserName { get; init; }
    public bool ApplyEmpty { get; init; }
    public double? PricePerMillion { get; init; }
    public char Delimiter { get; init; } = '#';
    // Optimization flags
    public bool OptDedup { get; init; }
    public bool OptUseTm { get; init; }
    public string? OptTmPath { get; init; }
    public bool OptBatchCache { get; init; }
    public bool OptPlaceholders { get; init; }
    public bool OptHumanLoop { get; init; }
    public bool LegacyTsv { get; init; }
    // Global TM
    public bool? GtmEnabledOverride { get; init; }
    public string? GtmMode { get; init; } // append|merge|readonly
    public string? GtmNamespace { get; init; }
    public string? GtmImport { get; init; }
    public string? GtmExport { get; init; }
    public bool GtmLearn { get; init; }
    public string? GtmPriority { get; init; } // global|local

    public static CommandContext FromArgs(string[] args)
    {
        var cmd = args[0].ToLowerInvariant();
        string? GetOpt(string key)
        {
            var i = Array.FindIndex(args, a => a.Equals(key, StringComparison.OrdinalIgnoreCase));
            return (i >= 0 && i + 1 < args.Length) ? args[i + 1] : null;
        }

        var delim = new TableIo().ResolveDelimiter(GetOpt("--delimiter"), '#');

        double? ppm = null;
        var priceVal = GetOpt("--price") ?? GetOpt("--price-per-million");
        if (!string.IsNullOrWhiteSpace(priceVal))
        {
            if (double.TryParse(priceVal, NumberStyles.Float, CultureInfo.InvariantCulture, out var x) ||
                double.TryParse(priceVal, NumberStyles.Float, new CultureInfo("ru-RU"), out x))
                ppm = x;
        }

        bool? gtmOverride = null;
        var gtmo = GetOpt("--global-tm");
        if (!string.IsNullOrWhiteSpace(gtmo))
            gtmOverride = gtmo.Equals("on", StringComparison.OrdinalIgnoreCase) ? true : gtmo.Equals("off", StringComparison.OrdinalIgnoreCase) ? false : null;

        return new CommandContext
        {
            Command = cmd,
            Args = args,
            ConfigPath = GetOpt("--config"),
            GlossaryPath = GetOpt("--glossary"),
            ParserName = GetOpt("--parser"),
            ApplyEmpty = args.Any(a => a.Equals("--apply-empty", StringComparison.OrdinalIgnoreCase)),
            PricePerMillion = ppm,
            Delimiter = (args.Any(a => a.Equals("--legacy-tsv", StringComparison.OrdinalIgnoreCase)) ? '\t' : delim),
            LegacyTsv = args.Any(a => a.Equals("--legacy-tsv", StringComparison.OrdinalIgnoreCase)),
            OptDedup = args.Any(a => a.Equals("--dedup", StringComparison.OrdinalIgnoreCase)),
            OptUseTm = args.Any(a => a.Equals("--tm", StringComparison.OrdinalIgnoreCase)) || !string.IsNullOrEmpty(GetOpt("--tm")),
            OptTmPath = GetOpt("--tm"),
            OptBatchCache = args.Any(a => a.Equals("--batch-cache", StringComparison.OrdinalIgnoreCase)),
            OptPlaceholders = args.Any(a => a.Equals("--placeholders", StringComparison.OrdinalIgnoreCase)),
            OptHumanLoop = args.Any(a => a.Equals("--hl-review", StringComparison.OrdinalIgnoreCase)),
            GtmEnabledOverride = gtmOverride,
            GtmMode = GetOpt("--tm-mode"),
            GtmNamespace = GetOpt("--tm-namespace"),
            GtmImport = GetOpt("--tm-import"),
            GtmExport = GetOpt("--tm-export"),
            GtmLearn = args.Any(a => a.Equals("--tm-learn", StringComparison.OrdinalIgnoreCase)),
            GtmPriority = GetOpt("--tm-priority")
        };
    }
}
