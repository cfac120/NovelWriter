namespace NovelWriter.Core.Entities;

public class InterludeEntry
{
    public string Id { get; init; } = string.Empty;
    public string SourceType { get; init; } = string.Empty;
    public string Source { get; init; } = string.Empty;
    public string CoreFact { get; init; } = string.Empty;
    public string NarrativeHook { get; init; } = string.Empty;
    public string AdaptableThemes { get; init; } = string.Empty;
    public string SuggestedGenres { get; init; } = string.Empty;
    public int UsageCount { get; set; }
    public DateTime CreatedAt { get; init; } = DateTime.UtcNow;
}
