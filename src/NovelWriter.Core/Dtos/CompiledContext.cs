using NovelWriter.Core.Entities;
using NovelWriter.Core.ValueObjects;

namespace NovelWriter.Core.Dtos;

public class CompiledContext
{
    public string SystemPrompt { get; set; } = string.Empty;
    public string UserMessage { get; set; } = string.Empty;
    public TokenBudget TokenBudget { get; set; } = null!;
    public Outline? Outline { get; set; }
}
