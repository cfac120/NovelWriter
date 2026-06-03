namespace NovelWriter.Core.Entities;

public class StyleProfile
{
    public string Id { get; init; } = string.Empty;
    public string SourceTitle { get; init; } = string.Empty;
    public string SourceAuthor { get; init; } = string.Empty;
    public string SourceType { get; init; } = string.Empty;
    public int SourceWordCount { get; init; }
    public string ProfileJson { get; init; } = string.Empty;
    public string Tags { get; init; } = string.Empty;
    public int UsageCount { get; set; }
    public int? LastUsedChapter { get; set; }
    public DateTime CreatedAt { get; init; } = DateTime.UtcNow;
}
