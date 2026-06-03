namespace NovelWriter.Core.Entities;

/// <summary>
/// 风格使用日志。风格库是全局共享的，不绑定特定项目。
/// </summary>
public class StyleUsageLog
{
    public int Id { get; init; }
    public string StyleId { get; init; } = string.Empty;
    public int VolumeNumber { get; init; }
    public int ChapterNumber { get; init; }
    public DateTime UsedAt { get; init; } = DateTime.UtcNow;
}
