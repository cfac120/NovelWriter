using NovelWriter.Core.ValueObjects;

namespace NovelWriter.Core.Memory;

/// <summary>L1: 章节摘要</summary>
public class ChapterSummary
{
    public int Id { get; init; }
    public ProjectId ProjectId { get; init; } = null!;
    public int VolumeNumber { get; set; }
    public int ChapterNumber { get; set; }
    public string Summary { get; set; } = string.Empty;
    public int TokenCount { get; set; }
    public DateTime CreatedAt { get; init; } = DateTime.UtcNow;
}
