using NovelWriter.Core.Dtos;
using NovelWriter.Core.Memory;
using NovelWriter.Core.ValueObjects;

namespace NovelWriter.Core.Interfaces;

public interface IMemoryRepository
{
    // L3
    Task<IReadOnlyList<CharacterProfile>> GetCharacterProfilesAsync(ProjectId projectId);
    Task<CharacterProfile?> GetLatestCharacterProfileAsync(CharacterId id);
    Task AddCharacterProfileVersionAsync(CharacterProfile profile);
    Task<IReadOnlyList<WorldSetting>> GetWorldSettingsAsync(ProjectId projectId);
    Task<WorldSetting?> GetLatestWorldSettingAsync(WorldSettingId id);
    Task AddWorldSettingAsync(WorldSetting setting);

    // L2
    Task<IReadOnlyList<Foreshadowing>> GetActiveForeshadowingsAsync(ProjectId projectId, int volume);
    Task<IReadOnlyList<Foreshadowing>> GetAllForeshadowingsForVolumeAsync(ProjectId projectId, int volume);
    Task AddForeshadowingAsync(Foreshadowing fs);
    Task UpdateForeshadowingAsync(Foreshadowing fs);
    Task<IReadOnlyList<ArcTracker>> GetArcTrackersAsync(ProjectId projectId);
    Task UpdateArcTrackerAsync(ArcTracker arc);
    Task<IReadOnlyList<SubplotTracker>> GetSubplotTrackersAsync(ProjectId projectId, int volume);
    Task UpdateSubplotTrackerAsync(SubplotTracker subplot);

    // L1
    Task<IReadOnlyList<ChapterSummary>> GetRecentSummariesAsync(ProjectId projectId, int count, int beforeChapter = int.MaxValue);
    Task AddChapterSummaryAsync(ChapterSummary summary);

    // L2→L3 归档
    Task AddForeshadowingArchiveAsync(ForeshadowingArchive archive);
    Task AddVolumeSummaryAsync(VolumeSummary summary);

    // 事务写入
    Task WriteMemoryChangesAsync(
        MemoryExtractionResult extraction,
        IReadOnlyList<ConfirmationDecision> decisions);
}
