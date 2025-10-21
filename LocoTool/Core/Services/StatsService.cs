using LocoTool.Core.Abstractions;

namespace LocoTool.Core.Services;

/// <summary>Stats computation consistent with legacy behavior.</summary>
public sealed class StatsService : IStatsService
{
    private readonly ITableIo _tableIo;

    public StatsService(ITableIo tableIo)
    {
        _tableIo = tableIo;
    }

    public (long totalChars, int stringsCount) ComputeFromTable(string tablePath, char delimiter)
    {
        long total = 0;
        int cnt = 0;
        foreach (var r in _tableIo.ReadRows(tablePath, delimiter))
        {
            if (!string.IsNullOrWhiteSpace(r.OrigText) && string.IsNullOrWhiteSpace(r.TranslatedText))
            {
                total += r.OrigText.Length;
                cnt++;
            }
        }
        return (total, cnt);
    }

    public (int batches, double? exactCost, double? paddedCost, long? paddedChars) EstimateCost(
        long totalChars,
        int maxCharsPerRequest,
        double? pricePerMillion)
    {
        var batches = (int)Math.Ceiling(totalChars / (double)maxCharsPerRequest);
        if (pricePerMillion is double ppm)
        {
            var exact = (totalChars / 1_000_000.0) * ppm;
            var paddedChars = (long)batches * maxCharsPerRequest;
            var padded = (paddedChars / 1_000_000.0) * ppm;
            return (batches, exact, padded, paddedChars);
        }
        return (batches, null, null, null);
    }
}

