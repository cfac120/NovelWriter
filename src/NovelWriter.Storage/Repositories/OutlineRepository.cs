using Microsoft.EntityFrameworkCore;
using NovelWriter.Core.Entities;
using NovelWriter.Core.Interfaces;
using NovelWriter.Core.ValueObjects;

namespace NovelWriter.Storage.Repositories;

public class OutlineRepository : IOutlineRepository
{
    private readonly NovelWriterDbContext _db;

    public OutlineRepository(NovelWriterDbContext db) => _db = db;

    public async Task<IReadOnlyList<Outline>> GetByProjectAsync(ProjectId projectId) =>
        await _db.Outlines.Where(o => o.ProjectId == projectId)
            .OrderBy(o => o.VolumeNumber).ThenBy(o => o.ChapterNumber).ToListAsync();

    public async Task<IReadOnlyList<Outline>> GetByVolumeAsync(ProjectId projectId, int volumeNumber) =>
        await _db.Outlines.Where(o => o.ProjectId == projectId && o.VolumeNumber == volumeNumber)
            .OrderBy(o => o.ChapterNumber).ToListAsync();

    public async Task<Outline?> GetByChapterNumberAsync(ProjectId projectId, int volumeNumber, int chapterNumber) =>
        await _db.Outlines.FirstOrDefaultAsync(o =>
            o.ProjectId == projectId && o.VolumeNumber == volumeNumber && o.ChapterNumber == chapterNumber);

    public async Task AddAsync(Outline outline)
    {
        await _db.Outlines.AddAsync(outline);
        await _db.SaveChangesAsync();
    }

    public async Task UpdateAsync(Outline outline)
    {
        _db.Outlines.Update(outline);
        await _db.SaveChangesAsync();
    }
}
