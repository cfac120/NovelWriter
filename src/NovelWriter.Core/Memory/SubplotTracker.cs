using NovelWriter.Core.ValueObjects;

namespace NovelWriter.Core.Memory;

/// <summary>L2: 支线状态</summary>
public class SubplotTracker
{
    public int Id { get; init; }
    public ProjectId ProjectId { get; init; } = null!;
    public int VolumeNumber { get; set; }
    public string Name { get; set; } = string.Empty;
    public string Description { get; set; } = string.Empty;
    public bool WasMentionedThisChapter { get; set; }
    public int DanglingChapterCount { get; set; }
    public DateTime UpdatedAt { get; set; } = DateTime.UtcNow;
}
