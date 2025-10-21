using System.Text.RegularExpressions;
using LocoTool.Core.Abstractions;

namespace LocoTool.Core.Services;

public sealed class PlaceholderService : IPlaceholderService
{
    private static readonly Regex Numbers = new("[0-9]+", RegexOptions.Compiled);
    private static readonly Regex IsoDate = new("\\b(20[0-9]{2}|19[0-9]{2})[-/.](0?[1-9]|1[0-2])[-/.](0?[1-9]|[12][0-9]|3[01])\\b", RegexOptions.Compiled);
    private static readonly Regex GuidRe = new("[A-Fa-f0-9]{8}-([A-Fa-f0-9]{4}-){3}[A-Fa-f0-9]{12}", RegexOptions.Compiled);
    private static readonly Regex CurlyBlock = new("\\{[^}]+\\}", RegexOptions.Compiled);

    public string Mask(string input, out string[] placeholders)
    {
        var list = new List<string>();
        string s = input ?? string.Empty;

        // order: GUID, dates, curly, numbers
        s = GuidRe.Replace(s, m => { list.Add(m.Value); return Token(list.Count); });
        s = IsoDate.Replace(s, m => { list.Add(m.Value); return Token(list.Count); });
        s = CurlyBlock.Replace(s, m => { list.Add(m.Value); return Token(list.Count); });
        s = Numbers.Replace(s, m => { list.Add(m.Value); return Token(list.Count); });

        placeholders = list.ToArray();
        return s;
    }

    public string Unmask(string translated, string[] placeholders)
    {
        if (placeholders.Length == 0) return translated ?? string.Empty;
        var s = translated ?? string.Empty;
        for (int i = 0; i < placeholders.Length; i++)
        {
            s = s.Replace(Token(i + 1), placeholders[i]);
        }
        return s;
    }

    private static string Token(int n) => "{PH_" + n + "}";
}
