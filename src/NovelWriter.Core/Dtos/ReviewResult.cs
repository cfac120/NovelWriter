namespace NovelWriter.Core.Dtos;

public class ReviewResult
{
    public string PersonaName { get; set; } = string.Empty;
    public double Score { get; set; }
    public string Feedback { get; set; } = string.Empty;
    public string? Suggestions { get; set; }
}
