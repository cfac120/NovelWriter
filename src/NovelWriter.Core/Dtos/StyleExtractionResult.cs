namespace NovelWriter.Core.Dtos;

public class StyleExtractionResult
{
    public string SourceTitle { get; set; } = string.Empty;
    public string ProfileJson { get; set; } = string.Empty;
    public string Tags { get; set; } = string.Empty;
    public int EstimatedTokens { get; set; }
}
