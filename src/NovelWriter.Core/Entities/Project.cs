using NovelWriter.Core.Enums;
using NovelWriter.Core.ValueObjects;

namespace NovelWriter.Core.Entities;

public class Project
{
    public ProjectId Id { get; init; } = ProjectId.New();
    public string Title { get; set; } = string.Empty;
    public string? Genre { get; set; }
    public ProjectStatus Status { get; set; } = ProjectStatus.Draft;
    public int TargetWordCount { get; set; }
    public int TargetChapterCount { get; set; }
    public int ChaptersPerVolume { get; set; } = 30;
    public string? StoryIdea { get; set; }
    public string? CustomInstructions { get; set; }
    public DateTime CreatedAt { get; init; } = DateTime.UtcNow;
    public DateTime UpdatedAt { get; set; } = DateTime.UtcNow;
}
