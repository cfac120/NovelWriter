using Microsoft.EntityFrameworkCore;
using NovelWriter.Core.Entities;
using NovelWriter.Core.Interfaces;
using NovelWriter.Core.ValueObjects;

namespace NovelWriter.Storage.Repositories;

public class InterludeRepository : IInterludeRepository
{
    private readonly NovelWriterDbContext _db;

    public InterludeRepository(NovelWriterDbContext db) => _db = db;

    public async Task<IReadOnlyList<InterludeEntry>> GetAvailableInterludesAsync() =>
        await _db.InterludeEntries.ToListAsync();

    public async Task<InterludeEntry?> GetRandomUnusedInVolumeAsync(ProjectId projectId, int volumeNumber)
    {
        var usedIds = await _db.InterludeUsageLogs
            .Where(l => l.ProjectId == projectId && l.VolumeNumber == volumeNumber)
            .Select(l => l.InterludeId)
            .ToListAsync();

        var available = await _db.InterludeEntries
            .Where(e => !usedIds.Contains(e.Id))
            .ToListAsync();

        return available.Count == 0 ? null
            : available[Random.Shared.Next(available.Count)];
    }

    public async Task LogUsageAsync(string interludeId, ProjectId projectId, int volumeNumber, int chapterNumber)
    {
        _db.InterludeUsageLogs.Add(new InterludeUsageLog
        {
            InterludeId = interludeId,
            ProjectId = projectId,
            VolumeNumber = volumeNumber,
            ChapterNumber = chapterNumber,
            UsedAt = DateTime.UtcNow
        });
        await _db.SaveChangesAsync();
    }
}
