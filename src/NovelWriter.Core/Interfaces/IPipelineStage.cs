using NovelWriter.Core.Dtos;
using NovelWriter.Core.Enums;
using NovelWriter.Core.ValueObjects;

namespace NovelWriter.Core.Interfaces;

public interface IPipelineStage
{
    PipelineStage Stage { get; }
    Task<PipelineResult> ExecuteAsync(PipelineContext context, CancellationToken ct = default);
}

public record PipelineContext
{
    public required ProjectId ProjectId { get; init; }
    public PipelineStage CurrentStage { get; set; }
    public PipelineState State { get; init; } = new();
}

public class PipelineState
{
    public Dtos.TopicSelectionResult? TopicSelection { get; set; }
    public string? Synopsis { get; set; }
    public IReadOnlyList<Entities.Outline>? Outlines { get; set; }
    public int CurrentChapterNumber { get; set; }
    public int CurrentVolumeNumber { get; set; }
    public Dtos.CompiledContext? CompiledContext { get; set; }
    public Entities.StyleProfile? StyleProfile { get; set; }
    public Dtos.InterludePromptResult? InterludePrompt { get; set; }
    public Entities.Chapter? ChapterDraft { get; set; }
    public Dtos.MemoryExtractionResult? ExtractionResult { get; set; }
    public Dtos.AggregatedReview? AggregatedReview { get; set; }
    public Dtos.DetectionReport? DetectionReport { get; set; }
    public List<Dtos.IMemoryEntry>? L3SearchCache { get; set; }
    public List<Dtos.Deviation> IncrementalViolations { get; set; } = [];
    public IReadOnlyList<Dtos.ConfirmationDecision>? PendingDecisions { get; set; }
}

public record PipelineResult
{
    public bool Success { get; init; }
    public PipelineStage? NextStage { get; init; }
    public List<Dtos.ConfirmationItem> ConfirmationItems { get; init; } = [];
    public List<Dtos.ConfirmationItem> AutoConfirmedItems { get; init; } = [];
    public List<DomainEvent> Events { get; init; } = [];
    public bool RequiresConfirmation => ConfirmationItems.Count > 0;
    public static PipelineResult Completed => new() { Success = true, NextStage = null };
}
