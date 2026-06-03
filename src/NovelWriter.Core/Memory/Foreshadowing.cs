using NovelWriter.Core.Enums;
using NovelWriter.Core.ValueObjects;

namespace NovelWriter.Core.Memory;

/// <summary>L2: 伏笔追踪</summary>
public class Foreshadowing
{
    public int Id { get; init; }
    public ForeshadowingId ForeshadowingId { get; init; } = null!;
    public ProjectId ProjectId { get; init; } = null!;
    public int VolumeNumber { get; set; }
    public string Description { get; set; } = string.Empty;
    public ForeshadowingStatus Status { get; set; } = ForeshadowingStatus.Active;
    public ForeshadowingPriority Priority { get; set; } = ForeshadowingPriority.High;
    public PlantedBy PlantedBy { get; set; } = PlantedBy.AutoDetected;
    public int? PlantedChapter { get; set; }
    public int? ResolvedChapter { get; set; }
    public string? RelatedCharacterIds { get; set; }
    public DateTime CreatedAt { get; init; } = DateTime.UtcNow;
    public DateTime UpdatedAt { get; set; } = DateTime.UtcNow;
}
