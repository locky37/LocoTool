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
        char effDelim = delimiter;
        List<string> cols;
        if (delimiter == '#')
        {
            // auto-fallback: if no '#', but contains tab, warn and fallback
            if (!headerLine.Contains('#') && headerLine.Contains('\t'))
            {
                Console.WriteLine("[io] warning: no '#' in header, falling back to TSV");
                effDelim = '\t';
                cols = headerLine.Split('\t').ToList();
            }
            else
            {
                cols = HashCsv.ReadRow(headerLine, '#');
            }
        }
        else
        {
            cols = headerLine.Split(delimiter).ToList();
        }

        int iLine = cols.FindIndex(c => c.Equals("original_line_no", StringComparison.OrdinalIgnoreCase));
        int iField = cols.FindIndex(c => c.Equals("field_index", StringComparison.OrdinalIgnoreCase));
        int iOrig = cols.FindIndex(c => c.Equals("orig_text", StringComparison.OrdinalIgnoreCase));
        int iTrans = cols.FindIndex(c => c.Equals("translated_text", StringComparison.OrdinalIgnoreCase));

        if (iLine < 0 || iField < 0 || iOrig < 0 || iTrans < 0)
            throw new InvalidOperationException(
                $"В таблице должны быть колонки: original_line_no, field_index, orig_text, translated_text. " +
                $"Фактически: {string.Join(", ", cols)}");

        string? line;
        while ((line = sr.ReadLine()) != null)
        {
            List<string> c = effDelim == '#'
                ? HashCsv.ReadRow(line.TrimEnd('\r'), '#')
                : line.TrimEnd('\r').Split(effDelim).ToList();
            string Get(int idx) => (idx >= 0 && idx < c.Count) ? c[idx] : "";

            if (!int.TryParse(Get(iLine), NumberStyles.Integer, CultureInfo.InvariantCulture, out var lineNo)) continue;
            if (!int.TryParse(Get(iField), NumberStyles.Integer, CultureInfo.InvariantCulture, out var fieldIdx)) continue;

            yield return new Row(lineNo, fieldIdx, Get(iOrig), Get(iTrans));
        }
    }

    public void WriteRows(string path, char delimiter, IEnumerable<Row> rows)
    {
        using var sw = new StreamWriter(path, false, Encoding.UTF8);
        var header = new[] { "original_line_no", "field_index", "record_id_guess", "orig_text", "translated_text" };
        if (delimiter == '#') sw.WriteLine(HashCsv.WriteRow(header, '#'));
        else sw.WriteLine(string.Join(delimiter, header));
        foreach (var r in rows)
        {
            var fields = new[]
            {
                r.OriginalLineNo.ToString(CultureInfo.InvariantCulture),
                r.FieldIndex.ToString(CultureInfo.InvariantCulture),
                "",
                r.OrigText ?? string.Empty,
                r.TranslatedText ?? string.Empty
            };
            if (delimiter == '#') sw.WriteLine(HashCsv.WriteRow(fields, '#'));
            else sw.WriteLine(string.Join(delimiter, fields));
        }
        if (delimiter == '#') Console.WriteLine($"[io] writing hash-csv: {Path.GetFileName(path)} (rows: {rows.Count()})");
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

