using NovelWriter.Core.ValueObjects;

namespace NovelWriter.Core.Memory;

/// <summary>L2→L3: 已完结伏笔归档</summary>
public class ForeshadowingArchive
{
    public int Id { get; init; }
    public ForeshadowingId ForeshadowingId { get; init; } = null!;
    public ProjectId ProjectId { get; init; } = null!;
    public string Description { get; set; } = string.Empty;
    public string Resolution { get; set; } = string.Empty;
    public int PlantedChapter { get; set; }
    public int ResolvedChapter { get; set; }
    public string? KeyInsights { get; set; }
    public DateTime ArchivedAt { get; init; } = DateTime.UtcNow;
}
