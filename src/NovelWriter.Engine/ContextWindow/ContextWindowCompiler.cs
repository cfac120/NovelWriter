using System.Text;
using System.Text.Json;
using NovelWriter.Core.Dtos;
using NovelWriter.Core.Entities;
using NovelWriter.Core.Enums;
using NovelWriter.Core.Interfaces;
using NovelWriter.Core.Memory;
using NovelWriter.Core.ValueObjects;
using Serilog;

namespace NovelWriter.Engine.ContextWindow;

/// <summary>
/// ContextWindow 编译器。负责每章写作前将 L1/L2/L3 记忆 + 大纲 + 系统指令
/// 组装成 System Prompt（≤160K token）。
/// </summary>
public class ContextWindowCompiler
{
    private readonly IMemoryRepository _memoryRepo;
    private readonly IChapterRepository _chapterRepo;
    private readonly IOutlineRepository _outlineRepo;
    private readonly TokenCounter _tokenCounter;

    private const int MaxSystemPromptTokens = 160_000;

    public ContextWindowCompiler(
        IMemoryRepository memoryRepo,
        IChapterRepository chapterRepo,
        IOutlineRepository outlineRepo,
        TokenCounter tokenCounter)
    {
        _memoryRepo = memoryRepo;
        _chapterRepo = chapterRepo;
        _outlineRepo = outlineRepo;
        _tokenCounter = tokenCounter;
    }

    /// <summary>
    /// 编译 L1+L2+L3+大纲 → System Prompt 基础版本 (不含风格约束和插曲)。
    /// </summary>
    public async Task<CompiledContext> CompileAsync(
        ProjectId projectId, int volumeNumber, int chapterNumber, string modelName, CancellationToken ct)
    {
        Log.Information("Compiling ContextWindow for Project={ProjectId}, Chapter={Chapter}", projectId, chapterNumber);

        var outline = await _outlineRepo.GetByChapterNumberAsync(projectId, volumeNumber, chapterNumber)
            ?? throw new InvalidOperationException($"Outline not found for chapter {chapterNumber}");

        // 编译各层记忆
        var l1Section = await CompileL1Async(projectId, chapterNumber, modelName, ct);
        var l2Section = await CompileL2Async(projectId, volumeNumber, modelName, ct);
        var l3Section = await CompileL3Async(projectId, outline, chapterNumber, modelName, ct);

        var systemPrompt = SystemPromptBuilder.BuildWritingSystemPrompt(
            l3CharacterSection: l3Section,
            l3WorldSettingSection: "",
            l2Section: l2Section,
            l1Section: l1Section,
            toneDirective: $"本卷核心冲突基于大纲，保持叙事张力和情节连贯性。",
            styleDirective: "",
            writingInstructions: BuildWritingInstructions());

        var systemTokens = _tokenCounter.Estimate(systemPrompt, modelName);
        var userMessage = BuildUserMessage(outline);
        var userTokens = _tokenCounter.Estimate(userMessage, modelName);

        var budget = new TokenBudget(
            Total: MaxSystemPromptTokens,
            SystemPrompt: systemTokens - _tokenCounter.Estimate(l1Section, modelName) - _tokenCounter.Estimate(l2Section, modelName) - _tokenCounter.Estimate(l3Section, modelName),
            L1Context: _tokenCounter.Estimate(l1Section, modelName),
            L2Context: _tokenCounter.Estimate(l2Section, modelName),
            L3Context: _tokenCounter.Estimate(l3Section, modelName),
            UserMessage: userTokens,
            Reserved: (int)(MaxSystemPromptTokens * 0.05));

        Log.Information("ContextWindow compiled: System={SystemTokens}, User={UserTokens}, Budget={Budget:P1}",
            systemTokens, userTokens, budget.UsagePercent);

        return new CompiledContext
        {
            SystemPrompt = systemPrompt,
            UserMessage = userMessage,
            TokenBudget = budget,
            Outline = outline
        };
    }

    private async Task<string> CompileL1Async(
        ProjectId projectId, int currentChapter, string modelName, CancellationToken ct)
    {
        var summaries = await _memoryRepo.GetRecentSummariesAsync(projectId, 5, currentChapter);
        var sb = new StringBuilder();

        foreach (var s in summaries)
        {
            sb.AppendLine($"### 第{s.ChapterNumber}章摘要");
            sb.AppendLine(s.Summary);
            sb.AppendLine();
        }

        return sb.ToString();
    }

    private async Task<string> CompileL2Async(
        ProjectId projectId, int volumeNumber, string modelName, CancellationToken ct)
    {
        var sb = new StringBuilder();
        var foreshadowings = await _memoryRepo.GetActiveForeshadowingsAsync(projectId, volumeNumber);
        var arcs = await _memoryRepo.GetArcTrackersAsync(projectId);
        var subplots = await _memoryRepo.GetSubplotTrackersAsync(projectId, volumeNumber);

        if (foreshadowings.Count > 0)
        {
            sb.AppendLine("### 活跃伏笔");
            foreach (var fs in foreshadowings)
            {
                sb.AppendLine($"- [{fs.ForeshadowingId}] {fs.Description} (种于第{fs.PlantedChapter}章, 优先级:{fs.Priority})");
            }
            sb.AppendLine();
        }

        if (arcs.Count > 0)
        {
            sb.AppendLine("### 故事弧线");
            foreach (var arc in arcs)
            {
                sb.AppendLine($"- [{arc.ArcId}] {arc.Name}: {arc.Description}");
            }
            sb.AppendLine();
        }

        if (subplots.Count > 0)
        {
            sb.AppendLine("### 支线状态");
            foreach (var sub in subplots)
            {
                sb.AppendLine($"- {sub.Name}: {sub.Description} (悬空:{sub.DanglingChapterCount}章)");
            }
            sb.AppendLine();
        }

        return sb.ToString();
    }

    private async Task<string> CompileL3Async(
        ProjectId projectId, Outline outline, int currentChapter, string modelName, CancellationToken ct)
    {
        var sb = new StringBuilder();

        // 从大纲的 CharacterInvolvement 字段中提取人物
        if (!string.IsNullOrWhiteSpace(outline.CharacterInvolvement))
        {
            // CharacterInvolvement 可能是 JSON 数组或逗号分隔的 ID 列表
            var characterIds = ParseCharacterIds(outline.CharacterInvolvement);
            foreach (var charId in characterIds)
            {
                var profile = await _memoryRepo.GetLatestCharacterProfileAsync(charId);
                if (profile != null)
                {
                    sb.AppendLine($"### 人物: {profile.Name} [{profile.CharacterId}] (v{profile.Version})");
                    if (!string.IsNullOrWhiteSpace(profile.Profile))
                        sb.AppendLine(profile.Profile);
                    sb.AppendLine();
                }
            }
        }

        // 关键词补充检索（如果 IMemoryRepository 有 SearchL3ByKeywordsAsync）
        if (!string.IsNullOrWhiteSpace(outline.SceneDescription))
        {
            var keywords = NovelWriter.Core.DomainServices.KeywordExtractor.Extract(outline.SceneDescription, maxKeywords: 5);
            if (keywords.Count > 0)
            {
                sb.AppendLine("### 关键词相关设定");
                sb.AppendLine($"关键词: {string.Join(", ", keywords)}");
                // L3 关键词检索接口目前未在 IMemoryRepository 中定义
                // 后续在接口扩展后实现
            }
        }

        return sb.ToString();
    }

    private static List<CharacterId> ParseCharacterIds(string involvement)
    {
        var ids = new List<CharacterId>();
        // 尝试解析 JSON 数组
        try
        {
            var arr = JsonSerializer.Deserialize<string[]>(involvement);
            if (arr != null)
            {
                foreach (var idStr in arr)
                {
                    if (int.TryParse(idStr.Replace("CHAR_", "").Trim(), out var n) && n > 0)
                        ids.Add(new CharacterId(n));
                }
                return ids;
            }
        }
        catch { }

        // 尝试逗号分隔
        foreach (var part in involvement.Split(',', ';'))
        {
            var trimmed = part.Trim().Replace("CHAR_", "");
            if (int.TryParse(trimmed, out var n) && n > 0)
                ids.Add(new CharacterId(n));
        }

        return ids;
    }

    private static string BuildWritingInstructions()
    {
        return """
            1. 严格遵循大纲中规划的关键事件和冲突
            2. 人物行为必须符合其核心特质，绝对不能出现禁止特质
            3. 自然处理伏笔回收和新伏笔种入
            4. 章节结尾设置悬念或钩子
            5. 场景转换流畅，时间跳跃需自然过渡
            6. 对话体现角色个性，避免千人一面
            7. 必须严格控制在3000-4000字，超过4000字直接截断
            """;
    }

    private static string BuildUserMessage(Outline outline)
    {
        var sb = new StringBuilder();
        sb.AppendLine("## 本章大纲");
        sb.AppendLine($"卷{outline.VolumeNumber} 第{outline.ChapterNumber}章");
        sb.AppendLine("【硬性要求: 本章总字数不超过4000字，超过部分将被截断】");
        sb.AppendLine();
        if (!string.IsNullOrWhiteSpace(outline.SceneDescription))
            sb.AppendLine($"场景: {outline.SceneDescription}");
        if (!string.IsNullOrWhiteSpace(outline.KeyEvents))
            sb.AppendLine($"关键事件: {outline.KeyEvents}");
        if (!string.IsNullOrWhiteSpace(outline.CharacterInvolvement))
            sb.AppendLine($"涉及人物: {outline.CharacterInvolvement}");
        if (!string.IsNullOrWhiteSpace(outline.ForeshadowingNotes))
            sb.AppendLine($"伏笔提示: {outline.ForeshadowingNotes}");

        return sb.ToString();
    }
}
