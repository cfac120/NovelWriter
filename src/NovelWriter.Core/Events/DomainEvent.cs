namespace NovelWriter.Core;

public abstract record DomainEvent
{
    public DateTime OccurredAt { get; init; } = DateTime.UtcNow;
}

public record MemoryWriteRequested(string ProjectId, string Reason) : DomainEvent;
public record MemoryConfirmed(string ProjectId, string ItemId, bool Approved) : DomainEvent;
public record PipelineStageChanged(string ProjectId, int FromStage, int ToStage) : DomainEvent;
public record ForeshadowingDetected(string ProjectId, string ForeshadowingId, string Description) : DomainEvent;
