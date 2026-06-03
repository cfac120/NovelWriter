namespace NovelWriter.Core.Entities;

public class Persona
{
    public int Id { get; init; }
    public string Name { get; init; } = string.Empty;
    public string Role { get; init; } = string.Empty;
    public string SystemPrompt { get; init; } = string.Empty;
    public bool IsActive { get; set; } = true;
}
