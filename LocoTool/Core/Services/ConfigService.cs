using LocoTool.Config;
using LocoTool.Core.Abstractions;

namespace LocoTool.Core.Services;

/// <summary>Loads AppConfig from file.</summary>
public sealed class ConfigService : IConfigService
{
    public AppConfigResult Load(string? path)
    {
        try
        {
            var cfg = AppConfig.Load(path);
            return AppConfigResult.Ok(cfg);
        }
        catch (Exception ex)
        {
            return AppConfigResult.Fail(ex.Message);
        }
    }
}

