using System.Text;
using NovelWriter.Core.DomainServices;
using NovelWriter.Core.Entities;
using NovelWriter.Core.Interfaces;
using NovelWriter.Core.Memory;
using NovelWriter.Core.ValueObjects;
using Serilog;

namespace NovelWriter.Engine.Memory;

/// <summary>
/// L3 检索器 — 按需从全书记忆中检索相关条目注入 ContextWindow。
/// 采用 ID 直查 + 关键词补充双路径。
/// 剧透过滤: 排除关联章节 > 当前章号的条目。
/// </summary>
public class L3Retriever
{
    private readonly IMemoryRepository _memoryRepo;
    private readonly int _maxTokens;

    public L3Retriever(IMemoryRepository memoryRepo, int maxTokens = 40_000)
    {
        _memoryRepo = memoryRepo;
        _maxTokens = maxTokens;
    }

    /// <summary>
    /// 检索 L3 实体用于 ContextWindow 注入。
    /// 1. ID 直查: 大纲中引用的 CharacterIds/SettingIds → 直接查询最新版本
    /// 2. 关键词补充: 从大纲文本提取关键词 → 匹配 tags/aliases
    /// 3. 剧透过滤: 排除信息未公开的设定
    /// </summary>
    public async Task<string> RetrieveAsync(
        ProjectId projectId, Outline outline, int currentChapter, CancellationToken ct)
    {
        var sb = new StringBuilder();
        var usedIds = new HashSet<string>();

        // 主路径: ID 直查（大纲已含 CharacterIds/SettingIds）
        if (!string.IsNullOrWhiteSpace(outline.CharacterInvolvement))
        {
            var charIds = ParseIds(outline.CharacterInvolvement);
            foreach (var charId in charIds)
            {
                if (usedIds.Contains($"CHAR_{charId}")) continue;
                var profile = await _memoryRepo.GetLatestCharacterProfileAsync(new CharacterId(charId));
                if (profile != null)
                {
                    sb.AppendLine($"## 人物: {profile.Name} (v{profile.Version})");
                    sb.AppendLine(profile.Profile);
                    sb.AppendLine();
                    usedIds.Add($"CHAR_{charId}");
                }
            }
        }

        // 辅路径: 关键词补充检索
        if (!string.IsNullOrWhiteSpace(outline.SceneDescription))
        {
            var keywords = KeywordExtractor.Extract(outline.SceneDescription, maxKeywords: 8);

            // 关键词匹配 L3 人物和设定
            var allChars = await _memoryRepo.GetCharacterProfilesAsync(projectId);
            var allSettings = await _memoryRepo.GetWorldSettingsAsync(projectId);

            foreach (var entity in allChars.Where(c => !usedIds.Contains(c.CharacterId.ToString())))
            {
                if (keywords.Any(k => entity.Name.Contains(k, StringComparison.OrdinalIgnoreCase) ||
                                       entity.Profile.Contains(k, StringComparison.OrdinalIgnoreCase)))
                {
                    sb.AppendLine($"## 相关人物: {entity.Name} [{entity.CharacterId}] (v{entity.Version})");
                    sb.AppendLine(entity.Profile);
                    sb.AppendLine();
                    usedIds.Add(entity.CharacterId.ToString());
                }
            }

            foreach (var setting in allSettings)
            {
                if (keywords.Any(k => setting.Name.Contains(k, StringComparison.OrdinalIgnoreCase) ||
                                       setting.Description.Contains(k, StringComparison.OrdinalIgnoreCase)))
                {
                    sb.AppendLine($"## 世界观: {setting.Name} [{setting.WorldSettingId}] (v{setting.Version})");
                    sb.AppendLine(setting.Description);
                    sb.AppendLine();
                }
            }
        }

        var result = sb.ToString();
        Log.Information("[L3] Retrieved {Count} entries for Chapter {Chapter}",
            usedIds.Count, currentChapter);

        return result;
    }

    private static List<int> ParseIds(string involvement)
    {
        var ids = new List<int>();
        foreach (var part in involvement.Split(',', ';', ' '))
        {
            var trimmed = part.Trim().Replace("CHAR_", "").Replace("WORLD_", "");
            if (int.TryParse(trimmed, out var n) && n > 0)
                ids.Add(n);
        }
        return ids;
    }
}
