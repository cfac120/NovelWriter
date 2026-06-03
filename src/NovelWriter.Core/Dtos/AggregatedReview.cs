namespace NovelWriter.Core.Dtos;

public class AggregatedReview
{
    public double AverageScore { get; set; }
    public bool IsPassing { get; set; }
    public List<ReviewResult> Reviews { get; set; } = [];
    public string? Consensus { get; set; }
}
