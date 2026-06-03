using NovelWriter.Core.Dtos;
using NovelWriter.Core.Enums;
using NovelWriter.Core.Interfaces;
using NovelWriter.Core.ValueObjects;
using Serilog;

namespace NovelWriter.Engine.Review;

/// <summary>
/// 评审聚合器。加权聚合多个 Persona 的评审结果。
/// </summary>
public class ReviewAggregator
{
    private const double PassThreshold = 7.0;

    /// <summary>
    /// 聚合多个评审结果。
    /// OverallScore = 所有成功 Persona 的 overall 分数平均值。
    /// </summary>
    public AggregatedReview Aggregate(List<ReviewResult> results)
    {
        if (results == null || results.Count == 0)
            return new AggregatedReview { AverageScore = 0, IsPassing = false };

        var avg = results.Average(r => r.Score);

        return new AggregatedReview
        {
            AverageScore = avg,
            IsPassing = avg >= PassThreshold,
            Reviews = results,
            Consensus = DetermineConsensus(results)
        };
    }

    /// <summary>
    /// 判断是否需要润色。
    /// </summary>
    public bool NeedsRevision(AggregatedReview review) => !review.IsPassing;

    private static string DetermineConsensus(List<ReviewResult> results)
    {
        var passCount = results.Count(r => r.Score >= PassThreshold);
        var total = results.Count;

        if (passCount == total) return "全部评审者认为本章合格";
        if (passCount > total / 2) return $"大多数评审者({passCount}/{total})认为本章合格";
        if (passCount == total / 2) return $"评审意见分歧({passCount}/{total})";
        return $"大多数评审者({total - passCount}/{total})建议润色";
    }
}
