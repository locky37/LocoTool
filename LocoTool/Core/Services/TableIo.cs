using System.Globalization;
using System.Text;
using LocoTool.Core.Abstractions;

namespace LocoTool.Core.Services;

/// <summary>CSV/TSV/hash table IO consistent with legacy behavior.</summary>
public sealed class TableIo : ITableIo
{
    public IEnumerable<Row> ReadRows(string path, char delimiter)
    {
        using var sr = new StreamReader(path, Encoding.UTF8);
        var headerLine = (sr.ReadLine() ?? "").TrimEnd('\r');
        var cols = headerLine.Split(delimiter);

        int iLine = Array.FindIndex(cols, c => c.Equals("original_line_no", StringComparison.OrdinalIgnoreCase));
        int iField = Array.FindIndex(cols, c => c.Equals("field_index", StringComparison.OrdinalIgnoreCase));
        int iOrig = Array.FindIndex(cols, c => c.Equals("orig_text", StringComparison.OrdinalIgnoreCase));
        int iTrans = Array.FindIndex(cols, c => c.Equals("translated_text", StringComparison.OrdinalIgnoreCase));

        if (iLine < 0 || iField < 0 || iOrig < 0 || iTrans < 0)
            throw new InvalidOperationException(
                $"В таблице должны быть колонки: original_line_no, field_index, orig_text, translated_text. " +
                $"Фактически: {string.Join(", ", cols)}");

        string? line;
        while ((line = sr.ReadLine()) != null)
        {
            var c = line.TrimEnd('\r').Split(delimiter);
            string Get(int idx) => (idx >= 0 && idx < c.Length) ? c[idx] : "";

            if (!int.TryParse(Get(iLine), NumberStyles.Integer, CultureInfo.InvariantCulture, out var lineNo)) continue;
            if (!int.TryParse(Get(iField), NumberStyles.Integer, CultureInfo.InvariantCulture, out var fieldIdx)) continue;

            yield return new Row(lineNo, fieldIdx, Get(iOrig), Get(iTrans));
        }
    }

    public void WriteRows(string path, char delimiter, IEnumerable<Row> rows)
    {
        using var sw = new StreamWriter(path, false, Encoding.UTF8);
        sw.WriteLine(string.Join(delimiter, new[] { "original_line_no", "field_index", "record_id_guess", "orig_text", "translated_text" }));
        foreach (var r in rows)
        {
            sw.WriteLine(string.Join(delimiter, new[]
            {
                r.OriginalLineNo.ToString(CultureInfo.InvariantCulture),
                r.FieldIndex.ToString(CultureInfo.InvariantCulture),
                "", // record_id_guess (legacy placeholder)
                r.OrigText?.Replace("\n"," ") ?? string.Empty,
                r.TranslatedText?.Replace("\n"," ") ?? string.Empty
            }));
        }
    }

    public char ResolveDelimiter(string? s, char @default = '#')
    {
        if (string.IsNullOrEmpty(s)) return @default;
        return s switch
        {
            "\\t" => '\t',
            "tab" => '\t',
            "#" => '#',
            "," => ',',
            ";" => ';',
            "|" => '|',
            _ when s.Length == 1 => s[0],
            _ => @default
        };
    }
}

