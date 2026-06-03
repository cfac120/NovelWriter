using NovelWriter.Core.ValueObjects;

namespace NovelWriter.Core.Memory;

/// <summary>L3: 人物档案（可多版本）</summary>
public class CharacterProfile
{
    public int Id { get; init; }
    public CharacterId CharacterId { get; init; } = null!;
    public ProjectId ProjectId { get; init; } = null!;
    public string Name { get; set; } = string.Empty;
    public string Profile { get; set; } = string.Empty;
    public int Version { get; set; } = 1;
    public DateTime CreatedAt { get; init; } = DateTime.UtcNow;
}
