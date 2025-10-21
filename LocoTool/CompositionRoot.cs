using LocoTool.Cli;
using LocoTool.Core.Abstractions;
using LocoTool.Core.Services;
using LocoTool.Service;

namespace LocoTool;

/// <summary>
/// Creates application services and command router without external DI.
/// </summary>
public sealed class CompositionRoot
{
    public required CommandRouter Router { get; init; }

    /// <summary>
    /// Build all services and commands.
    /// </summary>
    public static CompositionRoot Build()
    {
        // Core services
        IConfigService config = new ConfigService();
        IGlossaryService glossary = new GlossaryService();
        ITableIo tableIo = new TableIo();
        IStatsService stats = new StatsService(tableIo);
        IParsingService parsing = new ParsingService();
        ITranslateClient? translate = null; // Provided by command when needed; tests can inject fake.

        // Commands
        var router = new CommandRouter(
            new ExtractCommand(parsing, tableIo, config),
            new TranslateCommand(config, glossary, translate, tableIo, stats),
            new ApplyCommand(parsing, config),
            new AllCommand(config, parsing, glossary, translate!, tableIo, stats),
            new StatsCommand(stats, config)
        );

        return new CompositionRoot { Router = router };
    }
}
