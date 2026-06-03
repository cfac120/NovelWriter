using System.Collections.Concurrent;
using NovelWriter.Core.Dtos;
using NovelWriter.Core.Entities;
using NovelWriter.Core.Enums;
using NovelWriter.Core.Interfaces;
using NovelWriter.Core.Memory;
using NovelWriter.Core.ValueObjects;
using NovelWriter.Engine.ContextWindow;
using NovelWriter.Engine.Memory;
using NovelWriter.Engine.Review;
using Serilog;

namespace NovelWriter.Engine.Pipeline;

/// <summary>
/// 流水线编排器。管理 9 阶段状态机推进、暂停/恢复、章节循环。
/// 使用 SemaphoreSlim(1,1) 保护上下文读写。
/// </summary>
public class PipelineOrchestrator
{
    private readonly IServiceProvider _serviceProvider;
    private readonly IProjectRepository _projectRepo;
    private readonly IChapterRepository _chapterRepo;
    private readonly IOutlineRepository _outlineRepo;
    private readonly IMemoryRepository _memoryRepo;
    private readonly ContextWindowCompiler _contextCompiler;
    private readonly MemoryManagerAgent _memoryAgent;
    private readonly L2Updater _l2Updater;
    private readonly MemoryChangeValidator _validator;
    private readonly ILlmAdapter _writingLlm;
    private readonly ILlmAdapter _reviewLlm;
    private readonly ReviewOrchestrator _reviewOrchestrator;
    private readonly SemaphoreSlim _lock = new(1, 1);
    private PipelineContext _context = null!;

    private const int MaxRevisionRounds = 3;

    public PipelineOrchestrator(
        IServiceProvider serviceProvider,
        IProjectRepository projectRepo,
        IChapterRepository chapterRepo,
        IOutlineRepository outlineRepo,
        IMemoryRepository memoryRepo,
        ContextWindowCompiler contextCompiler,
        MemoryManagerAgent memoryAgent,
        L2Updater l2Updater,
        MemoryChangeValidator validator,
        ILlmAdapter writingLlm,
        ILlmAdapter reviewLlm)
    {
        _serviceProvider = serviceProvider;
        _projectRepo = projectRepo;
        _chapterRepo = chapterRepo;
        _outlineRepo = outlineRepo;
        _memoryRepo = memoryRepo;
        _contextCompiler = contextCompiler;
        _memoryAgent = memoryAgent;
        _l2Updater = l2Updater;
        _validator = validator;
        _writingLlm = writingLlm;
        _reviewLlm = reviewLlm;
        _reviewOrchestrator = new ReviewOrchestrator(reviewLlm);
    }

    // === 启动流水线 ===

    public async Task<PipelineResult> StartAsync(ProjectId projectId, CancellationToken ct)
    {
        await _lock.WaitAsync(ct);
        try
        {
            _context = new PipelineContext { ProjectId = projectId };
            _context.CurrentStage = PipelineStage.Stage01_TopicSelection;

            Log.Information("Pipeline started for Project {ProjectId}", projectId);
            return await AdvanceAsync(ct);
        }
        finally { _lock.Release(); }
    }

    /// <summary>
    /// 确认恢复。接收批量 ConfirmationDecision 列表。
    /// </summary>
    public async Task<PipelineResult> ResumeWithDecisionsAsync(
        IReadOnlyList<ConfirmationDecision> decisions, CancellationToken ct)
    {
        await _lock.WaitAsync(ct);
        try
        {
            _context.State.PendingDecisions = decisions;
            Log.Information("Resuming pipeline with {Count} decisions", decisions.Count);

            return await AdvanceAsync(ct);
        }
        finally { _lock.Release(); }
    }

    // === 核心状态推进 ===

    private async Task<PipelineResult> AdvanceAsync(CancellationToken ct)
    {
        var stage = _context.CurrentStage;

        while (true)
        {
            var result = await ExecuteStageAsync(stage, ct);

            if (!result.Success)
            {
                Log.Warning("Stage {Stage} failed", stage);
                return result;
            }

            if (result.RequiresConfirmation)
            {
                _context.CurrentStage = stage;
                Log.Information("Stage {Stage} requires confirmation ({Count} items)",
                    stage, result.ConfirmationItems.Count);
                return result;
            }

            if (result.NextStage == null)
            {
                Log.Information("Pipeline completed for Project {ProjectId}", _context.ProjectId);
                return result;
            }

            stage = result.NextStage.Value;
            _context.CurrentStage = stage;
        }
    }

    private async Task<PipelineResult> ExecuteStageAsync(PipelineStage stage, CancellationToken ct)
    {
        return stage switch
        {
            PipelineStage.Stage01_TopicSelection => await ExecuteStage01Async(ct),
            PipelineStage.Stage02_SynopsisWriting => await ExecuteStage02Async(ct),
            PipelineStage.Stage03_OutlineWriting => await ExecuteStage03Async(ct),
            PipelineStage.Stage04_PreWritePrepare => await ExecuteStage04Async(ct),
            PipelineStage.Stage05_ChapterGenerate => await ExecuteStage05Async(ct),
            PipelineStage.Stage06_MemoryExtract => await ExecuteStage06Async(ct),
            PipelineStage.Stage07_ReviewPolish => await ExecuteStage07Async(ct),
            _ => new PipelineResult { Success = true, NextStage = null }
        };
    }

    // === Stage 04: 写前准备（ContextWindow 编译） ===

    private async Task<PipelineResult> ExecuteStage04Async(CancellationToken ct)
    {
        var chapterNumber = _context.State.CurrentChapterNumber;
        var volumeNumber = _context.State.CurrentVolumeNumber;

        Log.Information("[Stage04] Preparing ContextWindow for Volume{Vol} Chapter{Chap}", volumeNumber, chapterNumber);

        var compiledContext = await _contextCompiler.CompileAsync(
            _context.ProjectId, volumeNumber, chapterNumber, _writingLlm.ModelName, ct);

        _context.State.CompiledContext = compiledContext;

        return new PipelineResult
        {
            Success = true,
            NextStage = PipelineStage.Stage05_ChapterGenerate
        };
    }

    // === Stage 05: 章节生成 ===

    private async Task<PipelineResult> ExecuteStage05Async(CancellationToken ct)
    {
        var chapterNumber = _context.State.CurrentChapterNumber;
        var compiled = _context.State.CompiledContext
            ?? throw new InvalidOperationException("CompiledContext not set");

        Log.Information("[Stage05] Generating Chapter {Chapter}", chapterNumber);

        // 流式生成章节
        var sb = new System.Text.StringBuilder();
        await foreach (var chunk in _writingLlm.StreamChatAsync(
            compiled.SystemPrompt, compiled.UserMessage, ct))
        {
            sb.Append(chunk);
        }

        var content = sb.ToString();
        var chapter = new Chapter
        {
            ProjectId = _context.ProjectId,
            VolumeNumber = _context.State.CurrentVolumeNumber,
            ChapterNumber = chapterNumber,
            Title = compiled.Outline?.SceneDescription?.Truncate(30) ?? $"第{chapterNumber}章",
            Content = content,
            WordCount = content.Length,
            Status = ChapterStatus.Draft
        };

        await _chapterRepo.AddAsync(chapter);
        _context.State.ChapterDraft = chapter;

        Log.Information("[Stage05] Chapter {Chapter} generated: {Words} chars", chapterNumber, content.Length);

        return new PipelineResult
        {
            Success = true,
            NextStage = PipelineStage.Stage06_MemoryExtract
        };
    }

    // === Stage 06: 记忆提取 ===

    private async Task<PipelineResult> ExecuteStage06Async(CancellationToken ct)
    {
        var chapter = _context.State.ChapterDraft
            ?? throw new InvalidOperationException("ChapterDraft not set");
        var outline = _context.State.CompiledContext?.Outline
            ?? throw new InvalidOperationException("Outline not set");
        var chapterNumber = _context.State.CurrentChapterNumber;
        var volumeNumber = _context.State.CurrentVolumeNumber;

        Log.Information("[Stage06] Extracting memory for Chapter {Chapter}", chapterNumber);

        // Call 1: 摘要生成
        var summary = await _memoryAgent.GenerateSummaryAsync(
            _context.ProjectId, volumeNumber, chapterNumber, chapter, outline, ct);

        // Call 2: 结构检测
        var extraction = await _memoryAgent.ExtractStructuralChangesAsync(
            _context.ProjectId, volumeNumber, chapterNumber, chapter, outline, summary, ct);

        // 验证
        extraction = await _validator.ValidateAndFilterAsync(
            extraction, _context.ProjectId, volumeNumber, ct);

        _context.State.ExtractionResult = extraction;

        if (extraction.NeedsConfirmationItems.Count > 0)
        {
            return new PipelineResult
            {
                Success = true,
                NextStage = null, // 暂停等待确认
                ConfirmationItems = extraction.NeedsConfirmationItems,
                AutoConfirmedItems = extraction.AutoConfirmedItems
            };
        }

        // 无待确认项，自动写入
        await WriteMemoriesAsync(extraction, ct);
        return new PipelineResult { Success = true, NextStage = PipelineStage.Stage07_ReviewPolish };
    }

    private async Task WriteMemoriesAsync(MemoryExtractionResult extraction, CancellationToken ct)
    {
        await _memoryRepo.WriteMemoryChangesAsync(extraction, []);
        Log.Information("[Stage06] Memory changes written: {Auto} auto + {Pending} pending",
            extraction.AutoConfirmedItems.Count, extraction.NeedsConfirmationItems.Count);
    }

    // === Stage 07: 评审润色 ===

    private int _revisionRound;

    private async Task<PipelineResult> ExecuteStage07Async(CancellationToken ct)
    {
        var chapter = _context.State.ChapterDraft!;
        var outline = _context.State.CompiledContext?.Outline!;
        var compiled = _context.State.CompiledContext!;

        Log.Information("[Stage07] Reviewing Chapter {Chapter} (Revision round {Round})",
            _context.State.CurrentChapterNumber, _revisionRound + 1);

        var review = await _reviewOrchestrator.ReviewChapterAsync(
            chapter.Content,
            outline.SceneDescription ?? outline.KeyEvents ?? "",
            compiled.SystemPrompt.Truncate(500),
            personaCount: 4,
            ct: ct);

        _context.State.AggregatedReview = review;

        if (review.IsPassing)
        {
            Log.Information("[Stage07] Chapter passed review: {Score}/10", review.AverageScore);
            _revisionRound = 0;

            var nextChapter = _context.State.CurrentChapterNumber + 1;
            _context.State.CurrentChapterNumber = nextChapter;

            var outlines = _context.State.Outlines
                ?? await _outlineRepo.GetByProjectAsync(_context.ProjectId);

            if (nextChapter > outlines.Count)
                return PipelineResult.Completed;

            return new PipelineResult { Success = true, NextStage = PipelineStage.Stage04_PreWritePrepare };
        }

        _revisionRound++;
        if (_revisionRound >= MaxRevisionRounds)
        {
            Log.Warning("[Stage07] Max revision rounds ({Max}) reached for Chapter {Chapter}",
                MaxRevisionRounds, _context.State.CurrentChapterNumber);
            _revisionRound = 0;
            return new PipelineResult
            {
                Success = true,
                NextStage = null,
                ConfirmationItems =
                [
                    new ConfirmationItem
                    {
                        Type = "MaxRevisionReached",
                        Summary = $"已达到最大润色轮次({MaxRevisionRounds})，请手动决定"
                    }
                ]
            };
        }

        return new PipelineResult { Success = true, NextStage = PipelineStage.Stage05_ChapterGenerate };
    }

    // === Stage 01-03: 占位（Phase 1 实施时详细实现） ===

    private Task<PipelineResult> ExecuteStage01Async(CancellationToken ct) =>
        Task.FromResult(new PipelineResult { Success = true, NextStage = PipelineStage.Stage02_SynopsisWriting });

    private Task<PipelineResult> ExecuteStage02Async(CancellationToken ct) =>
        Task.FromResult(new PipelineResult { Success = true, NextStage = PipelineStage.Stage03_OutlineWriting });

    private Task<PipelineResult> ExecuteStage03Async(CancellationToken ct)
    {
        _context.State.CurrentChapterNumber = 1;
        _context.State.CurrentVolumeNumber = 1;
        return Task.FromResult(new PipelineResult { Success = true, NextStage = PipelineStage.Stage04_PreWritePrepare });
    }
}

internal static class TruncateExt
{
    public static string Truncate(this string value, int maxLength) =>
        string.IsNullOrEmpty(value) || value.Length <= maxLength ? value : value[..maxLength] + "...";
}
