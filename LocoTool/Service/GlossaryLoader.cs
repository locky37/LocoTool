using System.Text.Json;

namespace LocoTool.Service;
public static class GlossaryLoader
{
    public record GlossaryItem(string src, string dst, bool exact);

    /// <summary>
    /// Загружает глоссарий из указанного JSON-файла.
    /// Если файла нет — возвращает пустой массив.
    /// </summary>
    public static (string src, string dst, bool exact)[] Load(string? path = null)
    {
        path ??= Path.Combine(AppContext.BaseDirectory, "glossary.json");
        if (!File.Exists(path))
        {
            Console.WriteLine($"[GlossaryLoader] Файл не найден: {path}. Используется пустой глоссарий.");
            return Array.Empty<(string, string, bool)>();
        }

        try
        {
            var json = File.ReadAllText(path);
            var items = JsonSerializer.Deserialize<List<GlossaryItem>>(json,
                new JsonSerializerOptions { PropertyNameCaseInsensitive = true });

            if (items == null || items.Count == 0)
            {
                Console.WriteLine($"[GlossaryLoader] Пустой или некорректный JSON: {path}");
                return Array.Empty<(string, string, bool)>();
            }

            var tuples = new List<(string, string, bool)>();
            foreach (var item in items)
            {
                if (!string.IsNullOrWhiteSpace(item.src) && !string.IsNullOrWhiteSpace(item.dst))
                    tuples.Add((item.src, item.dst, item.exact));
            }

            Console.WriteLine($"[GlossaryLoader] Загружено терминов: {tuples.Count}");
            return tuples.ToArray();
        }
        catch (Exception ex)
        {
            Console.WriteLine($"[GlossaryLoader] Ошибка чтения {path}: {ex.Message}");
            return Array.Empty<(string, string, bool)>();
        }
    }
}
