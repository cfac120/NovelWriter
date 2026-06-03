namespace NovelWriter.Core.Dtos;

public class CompiledContext
{
    public string SystemPrompt { get; set; } = string.Empty;
    public string UserMessage { get; set; } = string.Empty;
    public int TotalTokens { get; set; }
    public int RemainingTokens { get; set; }
}
