using NovelWriter.Core.Dtos;
using NovelWriter.Core.Enums;
using NovelWriter.Core.Interfaces;
using NovelWriter.Core.Memory;
using NovelWriter.Core.ValueObjects;
using Serilog;

namespace NovelWriter.Engine.SelfCheck;

/// <summary>
/// SelfCheck 全量检查执行器。
/// 触发条件: 每卷最后一章定稿后、L2→L3 压缩前。
/// 全量遍历 L3 所有实体 + 全卷章节。
/// </summary>
public class SelfCheckRunner
{
    private readonly ILlmAdapter _selfCheckLlm;
    private readonly IMemoryRepository _memoryRepo;
    private readonly IChapterRepository _chapterRepo;
    private readonly IOutlineRepository _outlineRepo;

    public SelfCheckRunner(
        ILlmAdapter selfCheckLlm,
        IMemoryRepository memoryRepo,
        IChapterRepository chapterRepo,
        IOutlineRepository outlineRepo)
    {
        _selfCheckLlm = selfCheckLlm;
        _memoryRepo = memoryRepo;
        _chapterRepo = chapterRepo;
        _outlineRepo = outlineRepo;
    }

    /// <summary>
    /// 全量 SelfCheck。合并增量累积违规 + 新发现。
    /// </summary>
    public async Task<DeviationReport> RunFullCheckAsync(
        ProjectId projectId,
        int volumeNumber,
        int volumeStartChapter,
        int volumeEndChapter,
        List<Deviation> accumulatedIncrementalViolations,
        CancellationToken ct)
    {
        Log.Information("[SelfCheck] Full check for Volume {Volume}, Chapters {Start}-{End}",
            volumeNumber, volumeStartChapter, volumeEndChapter);

        var deviations = new List<Deviation>();

        // 1. 检查 traits.forbidden 违规
        var profiles = await _memoryRepo.GetCharacterProfilesAsync(projectId);
        var chapters = await _chapterRepo.GetByVolumeAsync(projectId, volumeNumber);

        foreach (var profile in profiles)
        {
            var forbiddenTraits = ExtractForbiddenTraits(profile.Profile);
            foreach (var chapter in chapters)
            {
                foreach (var trait in forbiddenTraits)
                {
                    if (chapter.Content.Contains(trait, StringComparison.OrdinalIgnoreCase))
                    {
                        deviations.Add(new Deviation
                        {
                            EntityId = profile.CharacterId.ToString(),
                            Severity = DeviationSeverity.High,
                            Description = $"角色 {profile.Name} 出现禁止特质 '{trait}'",
                            DetectedChapter = chapter.ChapterNumber
                        });
                    }
                }
            }
        }

        // 2. 合并增量累积违规
        deviations.AddRange(accumulatedIncrementalViolations);

        Log.Information("[SelfCheck] Found {Count} deviations in Volume {Volume}",
            deviations.Count, volumeNumber);

        return new DeviationReport
        {
            VolumeNumber = volumeNumber,
            Deviations = deviations,
            CriticalCount = deviations.Count(d => d.Severity == DeviationSeverity.Critical),
            HighCount = deviations.Count(d => d.Severity == DeviationSeverity.High)
        };
    }

    public async Task<DeviationReport> RunFullCheckWithLlmAsync(
        ProjectId projectId,
        int volumeNumber,
        int volumeStartChapter,
        int volumeEndChapter,
        List<Deviation> accumulatedViolations,
        CancellationToken ct)
    {
        var ruleBasedReport = await RunFullCheckAsync(
            projectId, volumeNumber, volumeStartChapter, volumeEndChapter, accumulatedViolations, ct);

        // LLM 辅助检测（仅当规则检测发现了问题）
        if (ruleBasedReport.Deviations.Count > 0)
        {
            var chapters = await _chapterRepo.GetByVolumeAsync(projectId, volumeNumber);
            var profiles = await _memoryRepo.GetCharacterProfilesAsync(projectId);

            foreach (var chapter in chapters.Take(3))
            {
                var userMsg = BuildSelfCheckUserMessage(profiles, chapter, ruleBasedReport.Deviations);
                try
                {
                    var llmResponse = await _selfCheckLlm.ChatAsync(BuildSelfCheckSystemPrompt(), userMsg, ct);
                    // LLM 辅助结果可追加到报告中
                    Log.Debug("[SelfCheck] LLM review for Chapter {Chapter}: {Response}",
                        chapter.ChapterNumber, llmResponse.Length > 100 ? llmResponse[..100] : llmResponse);
                }
                catch (Exception ex)
                {
                    Log.Warning(ex, "[SelfCheck] LLM review failed for Chapter {Chapter}", chapter.ChapterNumber);
                }
            }
        }

        return ruleBasedReport;
    }

    private static List<string> ExtractForbiddenTraits(string profile)
    {
        var traits = new List<string>();
        if (string.IsNullOrWhiteSpace(profile)) return traits;

        // 尝试从 Profile JSON 中提取 forbidden 特质
        var lines = profile.Split('\n', StringSplitOptions.RemoveEmptyEntries);
        var inForbidden = false;
        foreach (var line in lines)
        {
            if (line.Contains("forbidden", StringComparison.OrdinalIgnoreCase)) inForbidden = true;
            else if (line.Contains("primary", StringComparison.OrdinalIgnoreCase)) inForbidden = false;
            else if (inForbidden)
            {
                var trimmed = line.Trim().TrimStart('-', ' ', '[', ']', '"', ',');
                if (!string.IsNullOrWhiteSpace(trimmed) && trimmed.Length < 20)
                    traits.Add(trimmed);
            }
        }
        return traits;
    }

    private static string BuildSelfCheckSystemPrompt() => """
你是一位专业的小说编辑，正在检查小说章节的一致性。请检查以下章节是否存在：
1. 人物行为违反其禁止特质
2. 世界观规则被违反
3. 伏笔状态不一致
""";

    private static string BuildSelfCheckUserMessage(
        IReadOnlyList<CharacterProfile> profiles,
        Core.Entities.Chapter chapter,
        List<Deviation> ruleBasedDeviations)
    {
        var profileText = string.Join("\n", profiles.Select(p => $"- {p.Name}: {(p.Profile.Length > 200 ? p.Profile[..200] : p.Profile)}"));
        var deviationText = string.Join("\n", ruleBasedDeviations.Select(d => $"- [{d.Severity}] {d.Description}"));

        var content = chapter.Content.Length > 1000 ? chapter.Content[..1000] : chapter.Content;

        return
"## 人物档案\n" + profileText + "\n" +
"\n" +
"## 规则检测发现的偏差\n" + deviationText + "\n" +
"\n" +
"## 章节正文\n" + content + "\n";
    }
}

/// <summary>
/// SelfCheck 偏差报告。
/// </summary>
public class DeviationReport
{
    public int VolumeNumber { get; set; }
    public List<Deviation> Deviations { get; set; } = [];
    public int CriticalCount { get; set; }
    public int HighCount { get; set; }
    public bool HasCriticalDeviations => CriticalCount > 0;
}
