using System.Collections.Generic;

namespace LocoTool.Abstractions;

public sealed class ParserOptions
{
    public char TableDelimiter { get; init; } = '\t';      // разделитель таблицы обмена: \t | # | , ...
    public bool ApplyEmpty { get; init; } = false;         // затирать оригинал пустыми переводами
    public Dictionary<string, string>? Extra { get; init; } // формат-специфичные опции (если потребуются)
}

// Стандартизированная таблица обмена (колонки фиксируем договором)
public static class ExchangeTable
{
    // Имена колонок — DEF
    public const string ColLineNo = "original_line_no";
    public const string ColFieldIndex = "field_index";
    public const string ColRecordId = "record_id_guess";
    public const string ColOrigText = "orig_text";
    public const string ColTranslated = "translated_text";

    public static string Header(char delim) =>
        string.Join(delim, new[] { ColLineNo, ColFieldIndex, ColRecordId, ColOrigText, ColTranslated });
}

