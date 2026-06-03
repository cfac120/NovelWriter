using System.Text.Json;
using NovelWriter.Core.Dtos;
using NovelWriter.Core.Entities;
using NovelWriter.Core.Enums;
using NovelWriter.Core.Interfaces;
using NovelWriter.Core.Memory;
using NovelWriter.Core.ValueObjects;
using NovelWriter.Engine.ContextWindow;
using NovelWriter.Engine.Llm;
using Serilog;

namespace NovelWriter.Engine.Memory;

/// <summary>
/// Memory Manager Agent — 记忆管理的核心组件。
/// 每章写后执行 2 次独立 LLM 调用：
///   Call1 = GenerateSummaryAsync (摘要生成)
///   Call2 = ExtractStructuralChangesAsync (结构检测 B-F + SelfCheck)
/// 
/// 拆分理由: 摘要生成和结构检测是异构任务，拆分后 JSON schema 更简单、AI 遵守率更高。
/// </summary>
public class MemoryManagerAgent
{
    private readonly ILlmAdapter _extractionLlm;
    private readonly IMemoryRepository _memoryRepo;
    private readonly IChapterRepository _chapterRepo;
    private readonly TokenCounter _tokenCounter;
    private readonly string _modelName;

    public MemoryManagerAgent(
        ILlmAdapter extractionLlm,
        IMemoryRepository memoryRepo,
        IChapterRepository chapterRepo,
        TokenCounter tokenCounter,
        string modelName)
    {
        _extractionLlm = extractionLlm;
        _memoryRepo = memoryRepo;
        _chapterRepo = chapterRepo;
        _tokenCounter = tokenCounter;
        _modelName = modelName;
    }

    /// <summary>
    /// Call 1: 摘要生成。独立调用，确保 L1 基础可靠。
    /// 输入: 本章正文 + 本章大纲
    /// 输出: ChapterSummary (JSON)
    /// </summary>
    public async Task<ChapterSummary> GenerateSummaryAsync(
        ProjectId projectId, int volumeNumber, int chapterNumber, Chapter chapter, Outline outline, CancellationToken ct)
    {
        Log.Information("[Memory-Call1] Generating L1 summary for Chapter {Chapter}", chapterNumber);

        var systemPrompt = SystemPromptBuilder.BuildSummaryExtractionPrompt();
        var userMessage = BuildSummaryUserMessage(chapter, outline);

        string llmResponse;
        try
        {
            llmResponse = await _extractionLlm.ChatAsync(systemPrompt, userMessage, ct);
        }
        catch (Exception ex)
        {
            Log.Error(ex, "[Memory-Call1] LLM call failed for Chapter {Chapter}", chapterNumber);
            throw;
        }

        var parseResult = LlmResponseParser.Parse<SummaryExtractionDto>(llmResponse);
        if (!parseResult.Success || parseResult.Value == null)
        {
            Log.Warning("[Memory-Call1] Failed to parse summary, using fallback. Errors: {Errors}",
                string.Join("; ", parseResult.Errors));
            return CreateFallbackSummary(projectId, volumeNumber, chapterNumber, chapter);
        }

        var dto = parseResult.Value;

        return new ChapterSummary
        {
            ProjectId = projectId,
            VolumeNumber = volumeNumber,
            ChapterNumber = chapterNumber,
            Summary = dto.summary ?? $"第{chapterNumber}章摘要",
            TokenCount = _tokenCounter.Estimate(dto.summary ?? "", _modelName)
        };
    }

    /// <summary>
    /// Call 2: 结构检测。依赖 Call1 的 summary 作为输入。
    /// 任务 B-F + SelfCheck 合并为单次 JSON 输出。
    /// </summary>
    public async Task<MemoryExtractionResult> ExtractStructuralChangesAsync(
        ProjectId projectId, int volumeNumber, int chapterNumber,
        Chapter chapter, Outline outline, ChapterSummary summary, CancellationToken ct)
    {
        Log.Information("[Memory-Call2] Extracting structural changes for Chapter {Chapter}", chapterNumber);

        var systemPrompt = SystemPromptBuilder.BuildStructuralDetectionPrompt();
        var userMessage = await BuildStructuralUserMessage(projectId, volumeNumber, chapter, outline, summary, ct);

        string llmResponse;
        try
        {
            llmResponse = await _extractionLlm.ChatAsync(systemPrompt, userMessage, ct);
        }
        catch (Exception ex)
        {
            Log.Error(ex, "[Memory-Call2] LLM call failed for Chapter {Chapter}", chapterNumber);
            throw;
        }

        var parseResult = LlmResponseParser.Parse<StructuralExtractionDto>(llmResponse);
        if (!parseResult.Success || parseResult.Value == null)
        {
            Log.Warning("[Memory-Call2] Failed to parse structural detection, returning empty result");
            return CreateEmptyResult(summary);
        }

        var dto = parseResult.Value;
        var result = new MemoryExtractionResult { L1Summary = summary };

        // Task B: 伏笔回收检测
        if (dto.taskB_foreshadowing_resolutions != null)
        {
            foreach (var r in dto.taskB_foreshadowing_resolutions)
            {
                result.ForeshadowingResolutions.Add(new ForeshadowingResolution
                {
                    ForeshadowingId = r.foreshadowing_id,
                    Confidence = TryParseConfidence(r.confidence),
                    ResolvedChapter = chapterNumber,
                    Evidence = r.evidence
                });
            }
        }

        // Task C: 新伏笔候选
        if (dto.taskC_new_foreshadowings != null)
        {
            foreach (var fs in dto.taskC_new_foreshadowings)
            {
                result.NewForeshadowings.Add(new ForeshadowingCandidate
                {
                    Description = fs.description,
                    Priority = TryParsePriority(fs.priority),
                    RelatedCharacterIds = fs.related_characters != null
                        ? string.Join(",", fs.related_characters) : null
                });
            }
        }

        // Task D: ArcTracker 里程碑更新
        if (dto.taskD_arc_updates != null)
        {
            foreach (var a in dto.taskD_arc_updates)
            {
                result.ArcUpdates.Add(new ArcMilestoneUpdate
                {
                    ArcId = a.arc_id,
                    MilestoneName = a.milestone_reached,
                    Status = TryParseMilestoneStatus(a.new_status)
                });
            }
        }

        // Task E: SubplotTracker 更新
        if (dto.taskE_subplot_updates != null)
        {
            foreach (var s in dto.taskE_subplot_updates)
            {
                result.SubplotUpdates.Add(new SubplotUpdate
                {
                    SubplotName = s.subplot_name,
                    WasMentioned = s.mentioned,
                    DanglingCount = s.dangling_count
                });
            }
        }

        // Task F: L3 变更建议
        if (dto.taskF_l3_change_proposals != null)
        {
            foreach (var p in dto.taskF_l3_change_proposals)
            {
                result.L3ChangeProposals.Add(new L3ChangeProposal
                {
                    TargetId = p.target_id,
                    TargetType = p.target_type,
                    ChangeDescription = p.proposed_change,
                    Confidence = TryParseConfidence(p.confidence)
                });
            }
        }

        // SelfCheck: traits.forbidden 违规
        if (dto.selfcheck_forbidden_violations != null)
        {
            result.ForbiddenTraitViolations = new List<Deviation>();
            foreach (var v in dto.selfcheck_forbidden_violations)
            {
                result.ForbiddenTraitViolations.Add(new Deviation
                {
                    EntityId = v.entity_id,
                    Severity = TryParseSeverity(v.severity),
                    Description = v.violation,
                    DetectedChapter = chapterNumber
                });
            }
        }

        // 确认闸门分流
        CategorizeConfirmations(result);

        Log.Information("[Memory-Call2] Extracted: {Resolutions} resolutions, {NewFs} new foreshadowings, " +
            "{ArcUpdates} arc updates, {SubplotUpdates} subplot updates, {L3Changes} L3 proposals",
            result.ForeshadowingResolutions.Count, result.NewForeshadowings.Count,
            result.ArcUpdates.Count, result.SubplotUpdates.Count, result.L3ChangeProposals.Count);

        return result;
    }

    // === 分流逻辑 ===

    private void CategorizeConfirmations(MemoryExtractionResult result)
    {
        // 自动通过: L1 摘要
        result.AutoConfirmedItems.Add(new ConfirmationItem
        {
            Type = "L1Summary",
            Summary = $"第{result.L1Summary.ChapterNumber}章摘要自动写入"
        });

        // 自动通过: High 置信度伏笔回收
        foreach (var res in result.ForeshadowingResolutions)
        {
            var item = new ConfirmationItem { Summary = $"伏笔 {res.ForeshadowingId} 回收: 第{res.ResolvedChapter}章" };
            if (res.Confidence == Confidence.High)
            {
                item.Type = "AutoResolution";
                result.AutoConfirmedItems.Add(item);
            }
            else
            {
                item.Type = "LowConfidenceResolution";
                item.Payload = res;
                result.NeedsConfirmationItems.Add(item);
            }
        }

        // 自动通过 vs 需确认: 新伏笔 (auto_detected 全部需要确认，防止LLM误判闲笔为伏笔)
        foreach (var fs in result.NewForeshadowings)
        {
            result.NeedsConfirmationItems.Add(new ConfirmationItem
            {
                Type = "NewForeshadowing",
                Summary = $"新伏笔候选: {fs.Description.Truncate(50)}",
                Payload = fs
            });
        }

        // 自动通过: ArcTracker milestone 更新
        foreach (var arc in result.ArcUpdates)
        {
            result.AutoConfirmedItems.Add(new ConfirmationItem
            {
                Type = "ArcMilestoneUpdate",
                Summary = $"弧线 {arc.ArcId}: {arc.MilestoneName} → {arc.Status}"
            });
        }

        // 自动通过: SubplotTracker 更新
        foreach (var sub in result.SubplotUpdates)
        {
            result.AutoConfirmedItems.Add(new ConfirmationItem
            {
                Type = "SubplotUpdate",
                Summary = $"支线 {sub.SubplotName}: {(sub.WasMentioned ? "已提及" : "未提及")}, 悬空{sub.DanglingCount}章"
            });
        }

        // 自动通过/需确认: L3 变更
        foreach (var change in result.L3ChangeProposals)
        {
            var item = new ConfirmationItem
            {
                Summary = $"L3变更: {change.TargetType}.{change.TargetId} — {change.ChangeDescription.Truncate(50)}",
                Payload = change
            };

            if (change.Confidence == Confidence.High)
            {
                item.Type = "AutoL3Change";
                result.AutoConfirmedItems.Add(item);
            }
            else
            {
                item.Type = "L3ChangeProposal";
                result.NeedsConfirmationItems.Add(item);
            }
        }
    }

    // === 辅助方法 ===

    private static string BuildSummaryUserMessage(Chapter chapter, Outline outline)
    {
        return $"""
            ## 本章大纲
            卷{outline.VolumeNumber} 第{outline.ChapterNumber}章
            场景: {outline.SceneDescription}
            关键事件: {outline.KeyEvents ?? "无"}

            ## 本章正文
            {chapter.Content}
            """;
    }

    private async Task<string> BuildStructuralUserMessage(
        ProjectId projectId, int volumeNumber,
        Chapter chapter, Outline outline, ChapterSummary summary, CancellationToken ct)
    {
        var activeFs = await _memoryRepo.GetActiveForeshadowingsAsync(projectId, volumeNumber);
        var arcs = await _memoryRepo.GetArcTrackersAsync(projectId);
        var subplots = await _memoryRepo.GetSubplotTrackersAsync(projectId, volumeNumber);

        var l2Status = new System.Text.StringBuilder();
        l2Status.AppendLine("## 当前 L2 状态");
        l2Status.AppendLine($"活跃伏笔 ({activeFs.Count}条): {string.Join(", ", activeFs.Select(f => f.ForeshadowingId))}");
        l2Status.AppendLine($"故事弧线 ({arcs.Count}条): {string.Join(", ", arcs.Select(a => a.ArcId))}");
        l2Status.AppendLine($"活跃支线 ({subplots.Count}条): {string.Join(", ", subplots.Select(s => s.Name))}");

        var profiles = await _memoryRepo.GetCharacterProfilesAsync(projectId);
        var l3Chars = profiles.Select(p => $"{p.CharacterId}({p.Name})");
        l2Status.AppendLine($"L3 人物: {string.Join(", ", l3Chars)}");

        return $"""
            ## 本章大纲
            卷{outline.VolumeNumber} 第{outline.ChapterNumber}章
            场景: {outline.SceneDescription}
            关键事件: {outline.KeyEvents ?? "无"}

            ## Call1 摘要
            {summary.Summary}

            ## 本章正文
            {chapter.Content}

            {l2Status}
            """;
    }

    private static ChapterSummary CreateFallbackSummary(
        ProjectId projectId, int volumeNumber, int chapterNumber, Chapter chapter)
    {
        return new ChapterSummary
        {
            ProjectId = projectId,
            VolumeNumber = volumeNumber,
            ChapterNumber = chapterNumber,
            Summary = chapter.Content.Truncate(500),
            TokenCount = chapter.Content.Truncate(500).Length / 2
        };
    }

    private static MemoryExtractionResult CreateEmptyResult(ChapterSummary summary)
    {
        return new MemoryExtractionResult
        {
            L1Summary = summary,
            AutoConfirmedItems = [new ConfirmationItem { Type = "L1Summary", Summary = $"第{summary.ChapterNumber}章摘要" }]
        };
    }

    private static Confidence TryParseConfidence(string? value) =>
        string.Equals(value, "high", StringComparison.OrdinalIgnoreCase) ? Confidence.High : Confidence.Low;

    private static ForeshadowingPriority TryParsePriority(string? value) =>
        string.Equals(value, "high", StringComparison.OrdinalIgnoreCase)
            ? ForeshadowingPriority.High : ForeshadowingPriority.Low;

    private static ArcMilestoneStatus TryParseMilestoneStatus(string? value) => value?.ToLowerInvariant() switch
    {
        "reached" or "completed" => ArcMilestoneStatus.Reached,
        "missed" => ArcMilestoneStatus.Missed,
        _ => ArcMilestoneStatus.Pending
    };

    private static DeviationSeverity TryParseSeverity(string? value) => value?.ToLowerInvariant() switch
    {
        "critical" => DeviationSeverity.Critical,
        "high" => DeviationSeverity.High,
        "medium" => DeviationSeverity.Medium,
        _ => DeviationSeverity.Low
    };

    // === LLM JSON 输出 DTOs ===

    private record SummaryExtractionDto(
        string? summary,
        List<string>? key_events,
        int word_count,
        SceneStateDto? current_scene_state);

    private record SceneStateDto(
        string? location,
        List<string>? present_characters,
        string? time,
        string? scene_mood,
        List<string>? pending_conflicts);

    private record StructuralExtractionDto(
        List<ForeshadowingResolutionDto>? taskB_foreshadowing_resolutions,
        List<NewForeshadowingDto>? taskC_new_foreshadowings,
        List<ArcUpdateDto>? taskD_arc_updates,
        List<SubplotUpdateDto>? taskE_subplot_updates,
        List<L3ChangeDto>? taskF_l3_change_proposals,
        List<ViolationDto>? selfcheck_forbidden_violations);

    private record ForeshadowingResolutionDto(
        string foreshadowing_id, string confidence, string? evidence);

    private record NewForeshadowingDto(
        string description, string priority, List<string>? related_characters, string? evidence);

    private record ArcUpdateDto(
        string arc_id, string milestone_reached, string new_status);

    private record SubplotUpdateDto(
        string subplot_name, bool mentioned, int dangling_count);

    private record L3ChangeDto(
        string target_type, string target_id, string proposed_change, string confidence);

    private record ViolationDto(
        string entity_id, string severity, string violation);
}

internal static class StringExtensions
{
    public static string Truncate(this string value, int maxLength) =>
        string.IsNullOrEmpty(value) || value.Length <= maxLength ? value : value[..maxLength] + "...";
}
