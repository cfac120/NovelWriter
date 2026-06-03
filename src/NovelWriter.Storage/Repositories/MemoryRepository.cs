using Microsoft.EntityFrameworkCore;
using NovelWriter.Core.Dtos;
using NovelWriter.Core.Interfaces;
using NovelWriter.Core.Enums;
using NovelWriter.Core.Memory;
using NovelWriter.Core.ValueObjects;

namespace NovelWriter.Storage.Repositories;

public class MemoryRepository : IMemoryRepository
{
    private readonly NovelWriterDbContext _db;

    public MemoryRepository(NovelWriterDbContext db) => _db = db;

    public async Task<IReadOnlyList<CharacterProfile>> GetCharacterProfilesAsync(ProjectId projectId) =>
        await _db.CharacterProfiles.Where(c => c.ProjectId == projectId).ToListAsync();

    public async Task<CharacterProfile?> GetLatestCharacterProfileAsync(CharacterId id) =>
        await _db.CharacterProfiles.Where(c => c.CharacterId == id).OrderByDescending(c => c.Version).FirstOrDefaultAsync();

    public async Task AddCharacterProfileVersionAsync(CharacterProfile profile)
    {
        await _db.CharacterProfiles.AddAsync(profile);
        await _db.SaveChangesAsync();
    }

    public async Task<IReadOnlyList<WorldSetting>> GetWorldSettingsAsync(ProjectId projectId) =>
        await _db.WorldSettings.Where(w => w.ProjectId == projectId).ToListAsync();

    public async Task<WorldSetting?> GetLatestWorldSettingAsync(WorldSettingId id) =>
        await _db.WorldSettings.Where(w => w.WorldSettingId == id).OrderByDescending(w => w.Version).FirstOrDefaultAsync();

    public async Task AddWorldSettingAsync(WorldSetting setting)
    {
        await _db.WorldSettings.AddAsync(setting);
        await _db.SaveChangesAsync();
    }

    public async Task<IReadOnlyList<Foreshadowing>> GetActiveForeshadowingsAsync(ProjectId projectId, int volume) =>
        await _db.Foreshadowings.Where(f => f.ProjectId == projectId && f.VolumeNumber == volume && f.Status == ForeshadowingStatus.Active).ToListAsync();

    public async Task<IReadOnlyList<Foreshadowing>> GetAllForeshadowingsForVolumeAsync(ProjectId projectId, int volume) =>
        await _db.Foreshadowings.Where(f => f.ProjectId == projectId && f.VolumeNumber == volume).ToListAsync();

    public async Task AddForeshadowingAsync(Foreshadowing fs)
    {
        await _db.Foreshadowings.AddAsync(fs);
        await _db.SaveChangesAsync();
    }

    public async Task UpdateForeshadowingAsync(Foreshadowing fs)
    {
        _db.Foreshadowings.Update(fs);
        await _db.SaveChangesAsync();
    }

    public async Task<IReadOnlyList<ArcTracker>> GetArcTrackersAsync(ProjectId projectId) =>
        await _db.ArcTrackers.Where(a => a.ProjectId == projectId).ToListAsync();

    public async Task UpdateArcTrackerAsync(ArcTracker arc)
    {
        _db.ArcTrackers.Update(arc);
        await _db.SaveChangesAsync();
    }

    public async Task<IReadOnlyList<SubplotTracker>> GetSubplotTrackersAsync(ProjectId projectId, int volume) =>
        await _db.SubplotTrackers.Where(s => s.ProjectId == projectId && s.VolumeNumber == volume).ToListAsync();

    public async Task UpdateSubplotTrackerAsync(SubplotTracker subplot)
    {
        _db.SubplotTrackers.Update(subplot);
        await _db.SaveChangesAsync();
    }

    public async Task<IReadOnlyList<ChapterSummary>> GetRecentSummariesAsync(ProjectId projectId, int count, int beforeChapter = int.MaxValue) =>
        await _db.ChapterSummaries
            .Where(s => s.ProjectId == projectId && s.ChapterNumber < beforeChapter)
            .OrderByDescending(s => s.ChapterNumber)
            .Take(count)
            .ToListAsync();

    public async Task AddChapterSummaryAsync(ChapterSummary summary)
    {
        await _db.ChapterSummaries.AddAsync(summary);
        await _db.SaveChangesAsync();
    }

    public async Task AddForeshadowingArchiveAsync(ForeshadowingArchive archive)
    {
        await _db.ForeshadowingArchives.AddAsync(archive);
        await _db.SaveChangesAsync();
    }

    public async Task AddVolumeSummaryAsync(VolumeSummary summary)
    {
        await _db.VolumeSummaries.AddAsync(summary);
        await _db.SaveChangesAsync();
    }

    public async Task WriteMemoryChangesAsync(MemoryExtractionResult extraction, IReadOnlyList<ConfirmationDecision> decisions)
    {
        await using var transaction = await _db.Database.BeginTransactionAsync();
        try
        {
            // L1 摘要入库
            await _db.ChapterSummaries.AddAsync(extraction.L1Summary);

            // 自动确认项
            foreach (var item in extraction.AutoConfirmedItems)
            {
                await ApplyConfirmationAsync(item, approved: true);
            }

            // 用户决策项
            foreach (var item in extraction.NeedsConfirmationItems)
            {
                var decision = decisions.FirstOrDefault(d => d.ItemId == item.Id);
                if (decision != null && decision.Approved)
                {
                    await ApplyConfirmationAsync(item, approved: true);
                }
            }

            await _db.SaveChangesAsync();
            await transaction.CommitAsync();
        }
        catch
        {
            await transaction.RollbackAsync();
            throw;
        }
    }

    private Task ApplyConfirmationAsync(ConfirmationItem item, bool approved)
    {
        // 实际应用根据 item.Type 分发到不同处理逻辑
        // 这里只做框架，具体逻辑在需求提取阶段完善
        return Task.CompletedTask;
    }
}
