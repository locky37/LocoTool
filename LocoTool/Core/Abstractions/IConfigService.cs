namespace LocoTool.Core.Abstractions;

/// <summary>Provides loading and validation of application config.</summary>
public interface IConfigService
{
    AppConfigResult Load(string? path);
}

/// <summary>Result wrapper for config loading.</summary>
public readonly record struct AppConfigResult(bool Success, LocoTool.Config.AppConfig? Value, string? Error)
{
    public static AppConfigResult Ok(LocoTool.Config.AppConfig cfg) => new(true, cfg, null);
    public static AppConfigResult Fail(string err) => new(false, null, err);
}

