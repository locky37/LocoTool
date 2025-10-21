namespace LocoTool.Abstractions;

public interface ILocParser
{
    // Уникальное имя плагина (для --parser hash, --parser json и т.п.)
    string Name { get; }

    // Быстрый тест: может ли парсер обработать файл? (по расширению/сигнатуре/первым строкам)
    bool CanHandle(string path, string? sample = null);

    // Извлечь переводимые строки в стандартную «таблицу обмена» (одна строка — одна запись)
    // Возвращаем уже готовый текст таблицы (разделитель задаёт опция)
    string Extract(string inputText, ParserOptions options);

    // Применить переводы из «таблицы обмена» обратно к исходнику
    string Apply(string originalText, string tableText, ParserOptions options);
}

