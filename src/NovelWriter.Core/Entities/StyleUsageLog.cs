using NovelWriter.Core.ValueObjects;

namespace NovelWriter.Core.Entities;

public class StyleUsageLog
{
    public int Id { get; init; }
    public string StyleId { get; init; } = string.Empty;
    public ProjectId ProjectId { get; init; } = null!;
    public int VolumeNumber { get; init; }
    public int ChapterNumber { get; init; }
    public DateTime UsedAt { get; init; } = DateTime.UtcNow;
}
