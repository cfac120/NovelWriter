using NovelWriter.Core.ValueObjects;

namespace NovelWriter.Core.Entities;

public class Review
{
    public int Id { get; init; }
    public ChapterId ChapterId { get; init; } = null!;
    public string PersonaName { get; set; } = string.Empty;
    public double Score { get; set; }
    public string Feedback { get; set; } = string.Empty;
    public string? Suggestions { get; set; }
    public DateTime CreatedAt { get; init; } = DateTime.UtcNow;
}
