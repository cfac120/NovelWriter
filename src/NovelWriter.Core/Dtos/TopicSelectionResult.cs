namespace NovelWriter.Core.Dtos;

public class TopicSelectionResult
{
    public string Genre { get; set; } = string.Empty;
    public string? Tags { get; set; }
    public string? TargetWordCount { get; set; }
    public string CoreConflict { get; set; } = string.Empty;
    public string TargetAudience { get; set; } = string.Empty;
    public string Differentiation { get; set; } = string.Empty;
    public string? InitialWorldSettingSuggestion { get; set; }
    public MainCharacterInfo? MainCharacter { get; set; }
}

public class MainCharacterInfo
{
    public string Name { get; set; } = string.Empty;
    public List<string> Traits { get; set; } = [];
}
