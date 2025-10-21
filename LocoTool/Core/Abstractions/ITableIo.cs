namespace LocoTool.Core.Abstractions;

/// <summary>Read/write exchange tables.</summary>
public interface ITableIo
{
    IEnumerable<Row> ReadRows(string path, char delimiter);
    void WriteRows(string path, char delimiter, IEnumerable<Row> rows);
    char ResolveDelimiter(string? arg, char @default = '#');
}

/// <summary>Exchange table row.</summary>
public readonly record struct Row(int OriginalLineNo, int FieldIndex, string OrigText, string TranslatedText);

