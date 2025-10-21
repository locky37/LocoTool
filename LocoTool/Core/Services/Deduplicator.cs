using LocoTool.Core.Abstractions;

namespace LocoTool.Core.Services;

public sealed class Deduplicator : IDeduplicator
{
    public (List<string> unique, int[] mapToUnique) Deduplicate(IReadOnlyList<string> segments)
    {
        var dict = new Dictionary<string, int>(StringComparer.Ordinal);
        var unique = new List<string>();
        var map = new int[segments.Count];
        for (int i = 0; i < segments.Count; i++)
        {
            var s = segments[i] ?? string.Empty;
            if (!dict.TryGetValue(s, out var idx))
            {
                idx = unique.Count;
                unique.Add(s);
                dict[s] = idx;
            }
            map[i] = idx;
        }
        return (unique, map);
    }
}

