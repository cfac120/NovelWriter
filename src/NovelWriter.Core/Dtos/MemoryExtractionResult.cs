using NovelWriter.Core.Enums;
using NovelWriter.Core.Memory;

namespace NovelWriter.Core.Dtos;

public class MemoryExtractionResult
{
    public ChapterSummary L1Summary { get; set; } = null!;
    public List<ForeshadowingResolution> ForeshadowingResolutions { get; set; } = [];
    public List<ForeshadowingCandidate> NewForeshadowings { get; set; } = [];
    public List<ArcMilestoneUpdate> ArcUpdates { get; set; } = [];
    public List<SubplotUpdate> SubplotUpdates { get; set; } = [];
    public List<L3ChangeProposal> L3ChangeProposals { get; set; } = [];
    public List<Deviation>? ForbiddenTraitViolations { get; set; }

    public List<ConfirmationItem> AutoConfirmedItems { get; set; } = [];
    public List<ConfirmationItem> NeedsConfirmationItems { get; set; } = [];
}

public class ForeshadowingResolution
{
    public string ForeshadowingId { get; set; } = string.Empty;
    public Confidence Confidence { get; set; }
    public int ResolvedChapter { get; set; }
    public string? Evidence { get; set; }
}

public class ForeshadowingCandidate
{
    public string Description { get; set; } = string.Empty;
    public ForeshadowingPriority Priority { get; set; }
    public string? RelatedCharacterIds { get; set; }
    public string? RelatedWorldSettingIds { get; set; }
}

public class ArcMilestoneUpdate
{
    public string ArcId { get; set; } = string.Empty;
    public string MilestoneName { get; set; } = string.Empty;
    public ArcMilestoneStatus Status { get; set; }
}

public class SubplotUpdate
{
    public string SubplotName { get; set; } = string.Empty;
    public bool WasMentioned { get; set; }
    public int DanglingCount { get; set; }
}

public class L3ChangeProposal
{
    public string TargetId { get; set; } = string.Empty;
    public string TargetType { get; set; } = string.Empty; // "Character" | "WorldSetting"
    public string ChangeDescription { get; set; } = string.Empty;
    public Confidence Confidence { get; set; }
}

public class Deviation
{
    public string EntityId { get; set; } = string.Empty;
    public DeviationSeverity Severity { get; set; }
    public string Description { get; set; } = string.Empty;
    public int DetectedChapter { get; set; }
}

public class ConfirmationItem
{
    public string Id { get; set; } = Guid.NewGuid().ToString("N")[..8];
    public string Type { get; set; } = string.Empty; // "NewForeshadowing" | "Resolution" | "L3Change"
    public string Summary { get; set; } = string.Empty;
    public object? Payload { get; set; }
}

public class ConfirmationDecision
{
    public string ItemId { get; set; } = string.Empty;
    public bool Approved { get; set; }
    public string? Note { get; set; }
}

public interface IMemoryEntry
{
    string EntryId { get; }
    string EntryType { get; }
    string Content { get; }
}
