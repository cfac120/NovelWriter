using NovelWriter.Core.Entities;
using NovelWriter.Core.Interfaces;
using NovelWriter.Core.ValueObjects;
using Serilog;

namespace NovelWriter.Engine.Style;

/// <summary>
/// 随机风格注入器。
/// 每章写作前随机选取风格档案，生成 ~400 token 风格约束追加到 System Prompt。
/// </summary>
public class StyleInjector
{
    private readonly IStyleLibraryRepository _repo;

    public StyleInjector(IStyleLibraryRepository repo) => _repo = repo;

    /// <summary>
    /// 随机选择风格档案（排除当前卷已使用）。
    /// 返回风格档案 + 约 300-400 token 的风格约束块。
    /// </summary>
    public async Task<(StyleProfile? Profile, string PromptBlock)> SelectRandomStyleAsync(
        ProjectId projectId, int volumeNumber, int chapterNumber)
    {
        var style = await _repo.GetRandomUnUsedInVolumeAsync(projectId, volumeNumber);

        if (style == null)
        {
            Log.Debug("[StyleInjector] No available style for Volume {Vol} Chapter {Chap}",
                volumeNumber, chapterNumber);
            return (null, "");
        }

        var promptBlock = BuildStylePromptBlock(style);
        await _repo.LogUsageAsync(style.Id, projectId, volumeNumber, chapterNumber);

        Log.Information("[StyleInjector] Applied style '{Style}' ({Title}) for Chapter {Chap}",
            style.Id, style.SourceTitle, chapterNumber);

        return (style, promptBlock);
    }

    private static string BuildStylePromptBlock(StyleProfile profile)
    {
        return $"""
            ## 本章写作风格约束
            参考风格: {profile.SourceTitle}（作者: {profile.SourceAuthor}, 类型: {profile.SourceType})
            风格特征: {profile.ProfileJson.Truncate(300)}

            请在保持人物和剧情不变的前提下，尝试融入以上风格特征。
            """;
    }
}


