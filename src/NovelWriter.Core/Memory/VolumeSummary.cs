using NovelWriter.Core.ValueObjects;

namespace NovelWriter.Core.Memory;

/// <summary>L2→L3: 卷级压缩摘要</summary>
public class VolumeSummary
{
    public int Id { get; init; }
    public ProjectId ProjectId { get; init; } = null!;
    public int VolumeNumber { get; set; }
    public string Summary { get; set; } = string.Empty;
    public int TokenCount { get; set; }
    public DateTime CreatedAt { get; init; } = DateTime.UtcNow;
}
