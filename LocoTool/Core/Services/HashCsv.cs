using System.Text;

namespace LocoTool.Core.Services;

/// <summary>
/// Minimal CSV helper for hash-separated values with quoting.
/// Escaping rules:
/// - If a field contains delimiter, \n, \r or a quote, the field is wrapped in double quotes
/// - Quotes inside quoted field are doubled
/// </summary>
public static class HashCsv
{
    public static string WriteRow(IEnumerable<string?> fields, char delimiter = '#')
    {
        var sb = new StringBuilder();
        bool first = true;
        foreach (var f in fields)
        {
            if (!first) sb.Append(delimiter);
            first = false;
            var s = f ?? string.Empty;
            var needQuote = s.IndexOf(delimiter) >= 0 || s.Contains('\n') || s.Contains('\r') || s.Contains('"');
            if (needQuote)
            {
                sb.Append('"');
                foreach (var ch in s)
                {
                    if (ch == '"') sb.Append("\"");
                    else sb.Append(ch);
                }
                sb.Append('"');
            }
            else sb.Append(s);
        }
        return sb.ToString();
    }

    public static List<string> ReadRow(string line, char delimiter = '#')
    {
        var res = new List<string>();
        if (line is null) return res;
        int i = 0;
        while (i < line.Length)
        {
            if (line[i] == '"')
            {
                i++; var sb = new StringBuilder();
                while (i < line.Length)
                {
                    var ch = line[i++];
                    if (ch == '"')
                    {
                        if (i < line.Length && line[i] == '"') { sb.Append('"'); i++; }
                        else break; // end of quoted
                    }
                    else sb.Append(ch);
                }
                res.Add(sb.ToString());
                if (i < line.Length && line[i] == delimiter) i++; // skip delimiter
            }
            else
            {
                int start = i;
                while (i < line.Length && line[i] != delimiter) i++;
                res.Add(line.Substring(start, i - start));
                if (i < line.Length && line[i] == delimiter) i++;
            }
        }
        return res;
    }
}

