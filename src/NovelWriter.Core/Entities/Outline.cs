using NovelWriter.Core.ValueObjects;

namespace NovelWriter.Core.Entities;

public class Outline
{
    public int Id { get; init; }
    public ProjectId ProjectId { get; init; } = null!;
    public int VolumeNumber { get; set; }
    public int ChapterNumber { get; set; }
    public string SceneDescription { get; set; } = string.Empty;
    public string? KeyEvents { get; set; }
    public string? CharacterInvolvement { get; set; }
    public string? ForeshadowingNotes { get; set; }
    public int TargetWordCount { get; set; } = 3000;
}
