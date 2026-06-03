using NovelWriter.Core.ValueObjects;

namespace NovelWriter.Core.Memory;

/// <summary>L3: 世界观条目</summary>
public class WorldSetting
{
    public int Id { get; init; }
    public WorldSettingId WorldSettingId { get; init; } = null!;
    public ProjectId ProjectId { get; init; } = null!;
    public string Name { get; set; } = string.Empty;
    public string Description { get; set; } = string.Empty;
    public int Version { get; set; } = 1;
    public DateTime CreatedAt { get; init; } = DateTime.UtcNow;
}
