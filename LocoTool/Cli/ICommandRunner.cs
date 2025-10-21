using LocoTool.Core.Abstractions;

namespace LocoTool.Cli;

/// <summary>Command runner abstraction.</summary>
public interface ICommandRunner
{
    string Name { get; }
    Task<int> RunAsync(CommandContext context, CancellationToken cancellationToken);
}

/// <summary>Simple router that picks command by name.</summary>
public sealed class CommandRouter
{
    private readonly Dictionary<string, ICommandRunner> _map;

    public CommandRouter(params ICommandRunner[] commands)
    {
        _map = commands.ToDictionary(c => c.Name, StringComparer.OrdinalIgnoreCase);
    }

    public Task<int> RunAsync(CommandContext ctx, CancellationToken ct)
    {
        if (_map.TryGetValue(ctx.Command, out var cmd))
            return cmd.RunAsync(ctx, ct);
        Console.WriteLine("Unknown command. Use: extract | translate | apply | all | stats");
        return Task.FromResult(1);
    }
}

