using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Text.RegularExpressions;
using LocoTool.Abstractions;

namespace HashDelimited.Parser;

public sealed class HashDelimitedParser : ILocParser
{
    public string Name => "hash";

    private static readonly Regex Cjk = new(@"[\u3400-\u4DBF\u4E00-\u9FFF\uF900-\uFAFF]", RegexOptions.Compiled);

    public bool CanHandle(string path, string? sample = null)
    {
        // Эвристика: расширение .txt/.hash и наличие символа '#'
        var ext = Path.GetExtension(path).ToLowerInvariant();
        if (ext is ".txt" or ".hash" or ".dat")
        {
            var s = sample ?? ReadSample(path);
            return s.Contains('#');
        }
        return false;
    }

    public string Extract(string inputText, ParserOptions opt)
    {
        var delim = opt.TableDelimiter;
        var sb = new StringBuilder();
        sb.AppendLine(ExchangeTable.Header(delim));

        int lineNo = 0;
        foreach (var raw in inputText.Split('\n'))
        {
            lineNo++;
            var line = raw.TrimEnd('\r');
            var (fields, trailing) = SplitHashPreserveTrailing(line);
            var recId = fields.Count > 0 ? fields[0] : "";

            for (int idx = 0; idx < fields.Count; idx++)
            {
                var val = fields[idx];
                if (Cjk.IsMatch(val))
                {
                    sb.Append(lineNo).Append(delim)
                      .Append(idx).Append(delim)
                      .Append((recId.All(char.IsDigit) ? recId : "")).Append(delim)
                      .Append(val).Append(delim)
                      .AppendLine(""); // translated_text пуст
                }
            }
        }
        return sb.ToString();
    }

    public string Apply(string originalText, string tableText, ParserOptions opt)
    {
        // загружаем таблицу
        var mapStrict = new Dictionary<(int,int,string), string>();
        var mapLoose  = new Dictionary<(int,int), string>();

        using (var sr = new StringReader(tableText))
        {
            var header = sr.ReadLine() ?? "";
            var cols = header.Split(opt.TableDelimiter);
            int iLine  = Array.IndexOf(cols, ExchangeTable.ColLineNo);
            int iField = Array.IndexOf(cols, ExchangeTable.ColFieldIndex);
            int iOrig  = Array.IndexOf(cols, ExchangeTable.ColOrigText);
            int iTr    = Array.IndexOf(cols, ExchangeTable.ColTranslated);

            string? row;
            while ((row = sr.ReadLine()) != null)
            {
                var c = row.Split(opt.TableDelimiter);
                if (!int.TryParse(Get(c, iLine), out var ln)) continue;
                if (!int.TryParse(Get(c, iField), out var fi)) continue;
                var orig = Get(c, iOrig) ?? "";
                var tr   = Get(c, iTr) ?? "";
                mapStrict[(ln, fi, orig)] = tr;
                mapLoose[(ln, fi)] = tr;
            }
        }

        // применяем
        var outLines = new List<string>();
        int ln2 = 0;
        foreach (var raw in originalText.Split('\n'))
        {
            ln2++;
            var line = raw.TrimEnd('\r');
            var (fields, trailing) = SplitHashPreserveTrailing(line);
            for (int i = 0; i < fields.Count; i++)
            {
                var val = fields[i];
                if (mapStrict.TryGetValue((ln2, i, val), out var tr) ||
                    mapLoose.TryGetValue((ln2, i), out tr))
                {
                    if (tr == "" && !opt.ApplyEmpty) continue;
                    fields[i] = tr;
                }
            }
            outLines.Add(JoinHashPreserveTrailing(fields, trailing));
        }
        return string.Join(Environment.NewLine, outLines);
    }

    // ===== утилиты =====
    private static string Get(string[] arr, int idx) => (idx >= 0 && idx < arr.Length) ? arr[idx] : "";

    private static (List<string> fields, int trailing) SplitHashPreserveTrailing(string line)
    {
        int total = line.Count(ch => ch == '#');
        var parts = line.Split('#').ToList();
        int explained = Math.Max(0, parts.Count - 1);
        int trailing = total - explained;
        for (int i = 0; i < trailing; i++) parts.Add("");
        return (parts, trailing);
    }

    private static string JoinHashPreserveTrailing(List<string> fields, int trailing)
    {
        var s = string.Join('#', fields);
        if (trailing > 0) s += new string('#', trailing);
        return s;
    }

    private static string ReadSample(string path)
    {
        using var sr = new StreamReader(path, Encoding.UTF8, true);
        char[] buf = new char[2048];
        int n = sr.ReadBlock(buf, 0, buf.Length);
        return new string(buf, 0, n);
    }
}

