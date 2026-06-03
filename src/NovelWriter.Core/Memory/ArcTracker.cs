using NovelWriter.Core.Enums;
using NovelWriter.Core.ValueObjects;

namespace NovelWriter.Core.Memory;

/// <summary>L2: 故事弧线</summary>
public class ArcTracker
{
    public int Id { get; init; }
    public ArcId ArcId { get; init; } = null!;
    public ProjectId ProjectId { get; init; } = null!;
    public string Name { get; set; } = string.Empty;
    public string Description { get; set; } = string.Empty;
    public string Milestones { get; set; } = string.Empty; // JSON
    public DateTime UpdatedAt { get; set; } = DateTime.UtcNow;
}
