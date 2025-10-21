using LocoTool.Core.Abstractions;

namespace LocoTool.Core.Services;

public sealed class BatchPlanner : IBatchPlanner
{
    public List<List<int>> Plan(IReadOnlyList<string> texts, int maxCharsPerRequest)
    {
        var result = new List<List<int>>();
        var current = new List<int>();
        int sum = 0;
        for (int i = 0; i < texts.Count; i++)
        {
            var add = texts[i]?.Length ?? 0;
            if (sum + add > maxCharsPerRequest && current.Count > 0)
            {
                result.Add(current);
                current = new List<int>();
                sum = 0;
            }
            current.Add(i);
            sum += add;
        }
        if (current.Count > 0) result.Add(current);
        return result;
    }
}

