namespace LocoTool.Core.Abstractions;

/// <summary>Computes statistics and optional cost estimation.</summary>
public interface IStatsService
{
    (long totalChars, int stringsCount) ComputeFromTable(string tablePath, char delimiter);
    (int batches, double? exactCost, double? paddedCost, long? paddedChars) EstimateCost(
        long totalChars,
        int maxCharsPerRequest,
        double? pricePerMillion);
}

