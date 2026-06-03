namespace NovelWriter.Core.Dtos;

public class TopicSelectionResult
{
    public string CoreConflict { get; set; } = string.Empty;
    public string TargetAudience { get; set; } = string.Empty;
    public string Differentiation { get; set; } = string.Empty;
    public string? InitialWorldSettingSuggestion { get; set; }
}
