using NovelWriter.Core.Entities;
using NovelWriter.Core.ValueObjects;

namespace NovelWriter.Core.Interfaces;

public interface IOutlineRepository
{
    Task<IReadOnlyList<Outline>> GetByProjectAsync(ProjectId projectId);
    Task<IReadOnlyList<Outline>> GetByVolumeAsync(ProjectId projectId, int volumeNumber);
    Task<Outline?> GetByChapterNumberAsync(ProjectId projectId, int volumeNumber, int chapterNumber);
    Task AddAsync(Outline outline);
    Task UpdateAsync(Outline outline);
}
