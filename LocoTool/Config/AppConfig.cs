using System.Text.Json;

namespace LocoTool.Config;

public sealed class AppConfig
{
    public YandexConfig Yandex { get; set; } = new();
    public LimitConfig Limits { get; set; } = new();
    public FileDefaults Files { get; set; } = new();
    public ParsersConfig Parsers { get; set; } = new();
    public OptimizationConfig Optimization { get; set; } = new();
    public GlobalTmConfig GlobalTM { get; set; } = new();

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

public sealed class ParsersConfig
{
    public string Folder { get; set; } = "parsers";
    public List<string>? Assemblies { get; set; }
    public string? Default { get; set; }
}

public sealed class OptimizationConfig
{
    public bool Deduplicate { get; set; } = false;
    public bool UseTM { get; set; } = false;
    public string TMPath { get; set; } = "cache.json";
    public bool BatchJoin { get; set; } = false;
    public int MinLenToJoin { get; set; } = 3;
    public int MaxJoinChars { get; set; } = 10000;
    public bool CodeAware { get; set; } = false;
    public bool CompressFrequency { get; set; } = false;
    public bool BatchCache { get; set; } = false;
    public bool HumanLoop { get; set; } = false;
    public bool Placeholders { get; set; } = false;
}

public sealed class GlobalTmConfig
{
    public bool Enabled { get; set; } = false;
    public string RootPath { get; set; } = "~/.locotool/gtm";
    public string ShardBy { get; set; } = "langpair";
    public string WritePolicy { get; set; } = "append"; // append|merge|readonly
    public string Namespace { get; set; } = "default";
    public double MinConfidence { get; set; } = 0.85;
    public bool PreferHumanEdited { get; set; } = true;
}
