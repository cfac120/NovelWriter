using NovelWriter.Core.ValueObjects;

namespace NovelWriter.Core.DomainServices;

public static class ReviewScoreCalculator
{
    public static ReviewScore CalculateAverage(IEnumerable<double> scores)
    {
        var list = scores.ToList();
        if (list.Count == 0) return ReviewScore.Zero;
        return new ReviewScore(list.Average());
    }

    public static ReviewScore CalculateWeighted(IEnumerable<(double Score, double Weight)> scoredWeights)
    {
        var list = scoredWeights.ToList();
        if (list.Count == 0) return ReviewScore.Zero;
        var totalWeight = list.Sum(x => x.Weight);
        if (totalWeight == 0) return ReviewScore.Zero;
        var weightedAvg = list.Sum(x => x.Score * x.Weight) / totalWeight;
        return new ReviewScore(weightedAvg);
    }
}
