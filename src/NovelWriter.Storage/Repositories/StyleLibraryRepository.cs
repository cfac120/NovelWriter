using Microsoft.EntityFrameworkCore;
using NovelWriter.Core.Entities;
using NovelWriter.Core.Interfaces;
using NovelWriter.Core.ValueObjects;

namespace NovelWriter.Storage.Repositories;

public class StyleLibraryRepository : IStyleLibraryRepository
{
    private readonly NovelWriterDbContext _db;

    public StyleLibraryRepository(NovelWriterDbContext db) => _db = db;

    public async Task<IReadOnlyList<StyleProfile>> GetAvailableStylesAsync() =>
        await _db.StyleProfiles.ToListAsync();

    public async Task<StyleProfile?> GetRandomUnUsedInVolumeAsync(ProjectId projectId, int volumeNumber)
    {
        var usedIds = await _db.StyleUsageLogs
            .Where(l => l.ProjectId == projectId && l.VolumeNumber == volumeNumber)
            .Select(l => l.StyleId)
            .ToListAsync();

        var available = await _db.StyleProfiles
            .Where(s => !usedIds.Contains(s.Id))
            .ToListAsync();

        return available.Count == 0 ? null
            : available[Random.Shared.Next(available.Count)];
    }

    public async Task LogUsageAsync(string styleId, ProjectId projectId, int volumeNumber, int chapterNumber)
    {
        _db.StyleUsageLogs.Add(new StyleUsageLog
        {
            StyleId = styleId,
            ProjectId = projectId,
            VolumeNumber = volumeNumber,
            ChapterNumber = chapterNumber,
            UsedAt = DateTime.UtcNow
        });
        await _db.SaveChangesAsync();
    }
}
