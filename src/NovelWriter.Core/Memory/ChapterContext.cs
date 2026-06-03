namespace NovelWriter.Core.Memory;

/// <summary>L1: 写作上下文（非持久化，每次编译生成）</summary>
public class ChapterContext
{
    public string RecentSummaries { get; set; } = string.Empty;
    public string CurrentSceneState { get; set; } = string.Empty;
    public int EstimatedTokens { get; set; }
}
