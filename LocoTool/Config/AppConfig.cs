using System.Text.Json;

namespace LocoTool.Config;

public sealed class AppConfig
{
    public YandexConfig Yandex { get; set; } = new();
    public LimitConfig Limits { get; set; } = new();
    public FileDefaults Files { get; set; } = new();

    public static AppConfig Load(string? path = null)
    {
        path ??= Path.Combine(AppContext.BaseDirectory, "config.json");
        if (!File.Exists(path))
            throw new FileNotFoundException($"Конфигурационный файл не найден: {path}");

        var json = File.ReadAllText(path);
        var cfg = JsonSerializer.Deserialize<AppConfig>(json,
            new JsonSerializerOptions { PropertyNameCaseInsensitive = true });
        if (cfg == null)
            throw new InvalidOperationException($"Ошибка чтения конфигурации: {path}");
        return cfg;
    }
}

public sealed class YandexConfig
{
    public string ApiKey { get; set; } = "";
    public string? FolderId { get; set; }
    public bool UseBearerToken { get; set; } = false;
    public string DefaultSourceLang { get; set; } = "zh";
    public string DefaultTargetLang { get; set; } = "en";
    public string GlossaryPath { get; set; } = "glossary.json";

    public string AuthHeader => UseBearerToken
        ? $"Bearer {ApiKey}"
        : $"Api-Key {ApiKey}";
}

public sealed class LimitConfig
{
    public int MaxCharsPerRequest { get; set; } = 10000;
    public int MaxGlossaryPairs { get; set; } = 50;
}

public sealed class FileDefaults
{
    public string DefaultInput { get; set; } = "input.txt";
    public string DefaultOutput { get; set; } = "output.txt";
}
