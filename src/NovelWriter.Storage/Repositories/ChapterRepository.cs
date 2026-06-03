using Microsoft.EntityFrameworkCore;
using NovelWriter.Core.Entities;
using NovelWriter.Core.Interfaces;
using NovelWriter.Core.ValueObjects;

namespace NovelWriter.Storage.Repositories;

public class ChapterRepository : IChapterRepository
{
    private readonly NovelWriterDbContext _db;

    public ChapterRepository(NovelWriterDbContext db) => _db = db;

    public async Task<Chapter?> GetByIdAsync(ChapterId id) =>
        await _db.Chapters.FindAsync(id);

    public async Task<IReadOnlyList<Chapter>> GetByProjectAsync(ProjectId projectId) =>
        await _db.Chapters.Where(c => c.ProjectId == projectId).OrderBy(c => c.VolumeNumber).ThenBy(c => c.ChapterNumber).ToListAsync();

    public async Task<IReadOnlyList<Chapter>> GetByVolumeAsync(ProjectId projectId, int volumeNumber) =>
        await _db.Chapters.Where(c => c.ProjectId == projectId && c.VolumeNumber == volumeNumber).OrderBy(c => c.ChapterNumber).ToListAsync();

    public async Task<Chapter?> GetByNumberAsync(ProjectId projectId, int volumeNumber, int chapterNumber) =>
        await _db.Chapters.FirstOrDefaultAsync(c => c.ProjectId == projectId && c.VolumeNumber == volumeNumber && c.ChapterNumber == chapterNumber);

    public async Task AddAsync(Chapter chapter)
    {
        await _db.Chapters.AddAsync(chapter);
        await _db.SaveChangesAsync();
    }

    public async Task UpdateAsync(Chapter chapter)
    {
        _db.Chapters.Update(chapter);
        await _db.SaveChangesAsync();
    }
}
