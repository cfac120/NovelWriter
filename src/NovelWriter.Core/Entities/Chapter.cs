using NovelWriter.Core.Enums;
using NovelWriter.Core.ValueObjects;

namespace NovelWriter.Core.Entities;

public class Chapter
{
    public ChapterId Id { get; init; } = ChapterId.New();
    public ProjectId ProjectId { get; init; } = null!;
    public int VolumeNumber { get; set; }
    public int ChapterNumber { get; set; }
    public string Title { get; set; } = string.Empty;
    public string Content { get; set; } = string.Empty;
    public int WordCount { get; set; }
    public ChapterStatus Status { get; set; } = ChapterStatus.Planned;
    public DateTime CreatedAt { get; init; } = DateTime.UtcNow;
    public DateTime UpdatedAt { get; set; } = DateTime.UtcNow;
}
