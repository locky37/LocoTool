using System.Text.RegularExpressions;
using LocoTool.Core.Abstractions;

namespace LocoTool.Core.Services;

public sealed class PlaceholderService : IPlaceholderService
{
    private static readonly Regex Numbers = new("[0-9]+", RegexOptions.Compiled);

    public string Mask(string input, out string[] placeholders)
    {
        var list = new List<string>();
        var s = Numbers.Replace(input ?? string.Empty, m =>
        {
            list.Add(m.Value);
            return "{NUM_" + list.Count + "}";
        });
        placeholders = list.ToArray();
        return s;
    }

    public string Unmask(string translated, string[] placeholders)
    {
        if (placeholders.Length == 0) return translated ?? string.Empty;
        var s = translated ?? string.Empty;
        for (int i = 0; i < placeholders.Length; i++)
        {
            s = s.Replace("{NUM_" + (i + 1) + "}", placeholders[i]);
        }
        return s;
    }
}

