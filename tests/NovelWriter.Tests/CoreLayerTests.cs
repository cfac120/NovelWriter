using NovelWriter.Core.DomainServices;
using NovelWriter.Core.Enums;
using NovelWriter.Core.ValueObjects;
using Xunit;

namespace NovelWriter.Tests;

public class TokenBudgetCalculatorTests
{
    [Fact]
    public void Calculate_WithValidInputs_ReturnsCorrectBudget()
    {
        var budget = TokenBudgetCalculator.Calculate(
            modelMaxTokens: 160000,
            systemPromptTokens: 2000,
            l1Tokens: 25000,
            l2Tokens: 5000,
            l3Tokens: 8000,
            userMessageTokens: 3000
        );

        Assert.Equal(160000, budget.Total);
        Assert.Equal(8000, budget.Reserved); // 5% of 160000
        Assert.Equal(2000 + 25000 + 5000 + 8000 + 3000 + 8000, budget.Used);
        Assert.True(budget.Remaining >= 0);
    }

    [Fact]
    public void IsWithinBudget_WhenOverBudget_ReturnsFalse()
    {
        var budget = TokenBudgetCalculator.Calculate(
            modelMaxTokens: 1000,
            systemPromptTokens: 400,
            l1Tokens: 300,
            l2Tokens: 200,
            l3Tokens: 100,
            userMessageTokens: 50
        );

        // Used = 400+300+200+100+50+50(reserved) = 1100 > 1000
        Assert.False(TokenBudgetCalculator.IsWithinBudget(budget));
    }
}

public class ReviewScoreCalculatorTests
{
    [Fact]
    public void CalculateAverage_WithMultipleScores_ReturnsCorrectAverage()
    {
        var scores = new[] { 7.5, 8.0, 6.5, 9.0 };
        var result = ReviewScoreCalculator.CalculateAverage(scores);

        Assert.Equal(7.75, result.Value, 2);
        Assert.True(result.IsPassing);
        Assert.False(result.IsExcellent);
    }

    [Fact]
    public void CalculateAverage_WithEmptyList_ReturnsZero()
    {
        var result = ReviewScoreCalculator.CalculateAverage([]);
        Assert.Equal(0, result.Value);
    }

    [Fact]
    public void ReviewScore_BelowThreshold_IsNotPassing()
    {
        var score = new ReviewScore(5.5);
        Assert.False(score.IsPassing);
    }
}

public class ConfidenceEvaluatorTests
{
    [Theory]
    [InlineData(0.8, Confidence.High)]
    [InlineData(0.7, Confidence.High)]
    [InlineData(0.69, Confidence.Low)]
    [InlineData(0.3, Confidence.Low)]
    public void FromScore_ReturnsCorrectConfidence(double score, Confidence expected)
    {
        Assert.Equal(expected, ConfidenceEvaluator.FromScore(score));
    }

    [Fact]
    public void ShouldAutoConfirm_HighConfidenceForeshadowing_ReturnsTrue()
    {
        Assert.True(ConfidenceEvaluator.ShouldAutoConfirm(Confidence.High, "NewForeshadowing"));
    }

    [Fact]
    public void ShouldAutoConfirm_LowConfidence_ReturnsFalse()
    {
        Assert.False(ConfidenceEvaluator.ShouldAutoConfirm(Confidence.Low, "NewForeshadowing"));
    }

    [Fact]
    public void ShouldAutoConfirm_L3Change_ReturnsFalse()
    {
        Assert.False(ConfidenceEvaluator.ShouldAutoConfirm(Confidence.High, "L3Change"));
    }
}

public class KeywordExtractorTests
{
    [Fact]
    public void Extract_WithChineseText_ReturnsKeywords()
    {
        var text = "修仙之路漫漫，修仙者追求大道，大道无形，修仙世界充满了各种奇遇";
        var keywords = KeywordExtractor.Extract(text, maxKeywords: 5);

        Assert.NotEmpty(keywords);
        // "修仙" appears 3 times, should be the top keyword
        Assert.Contains(keywords, k => k.Contains("修仙"));
    }

    [Fact]
    public void Extract_WithEmptyText_ReturnsEmptyList()
    {
        var keywords = KeywordExtractor.Extract("");
        Assert.Empty(keywords);
    }
}

public class ValueObjectTests
{
    [Fact]
    public void ProjectId_New_GeneratesUniqueId()
    {
        var id1 = ProjectId.New();
        var id2 = ProjectId.New();
        Assert.NotEqual(id1, id2);
    }

    [Fact]
    public void CharacterId_ToString_ReturnsFormattedId()
    {
        var id = new CharacterId(42);
        Assert.Equal("CHAR_042", id.ToString());
    }

    [Fact]
    public void ForeshadowingId_ToString_ReturnsFormattedId()
    {
        var id = new ForeshadowingId(7);
        Assert.Equal("FS_007", id.ToString());
    }

    [Fact]
    public void TokenBudget_UsagePercent_CalculatesCorrectly()
    {
        var budget = new TokenBudget(1000, 100, 200, 100, 50, 50, 50);
        Assert.Equal(55.0, budget.UsagePercent, 1);
    }

    [Fact]
    public void VersionNumber_NextMinor_IncrementsMinor()
    {
        var v = new VersionNumber(1, 3);
        var next = v.NextMinor();
        Assert.Equal(new VersionNumber(1, 4), next);
    }

    [Fact]
    public void VersionNumber_NextMajor_ResetsMinor()
    {
        var v = new VersionNumber(2, 5);
        var next = v.NextMajor();
        Assert.Equal(new VersionNumber(3, 0), next);
    }
}
