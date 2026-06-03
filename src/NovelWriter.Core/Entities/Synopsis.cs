using NovelWriter.Core.ValueObjects;

namespace NovelWriter.Core.Entities;

public class Synopsis
{
    public int Id { get; init; }
    public ProjectId ProjectId { get; init; } = null!;
    public string Content { get; set; } = string.Empty;
    public int Version { get; set; } = 1;
    public DateTime CreatedAt { get; init; } = DateTime.UtcNow;
}
