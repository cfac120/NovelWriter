namespace NovelWriter.Core.Dtos;

public class InterludePromptResult
{
    public string? InterludeId { get; set; }
    public string InterludePrompt { get; set; } = string.Empty;
    public InsertHint? Hint { get; set; }
}

public record InsertHint(double Ratio, string Description);
