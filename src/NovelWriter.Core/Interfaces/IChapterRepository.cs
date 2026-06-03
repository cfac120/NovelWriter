using NovelWriter.Core.Entities;
using NovelWriter.Core.ValueObjects;

namespace NovelWriter.Core.Interfaces;

public interface IChapterRepository
{
    Task<Chapter?> GetByIdAsync(ChapterId id);
    Task<IReadOnlyList<Chapter>> GetByProjectAsync(ProjectId projectId);
    Task<IReadOnlyList<Chapter>> GetByVolumeAsync(ProjectId projectId, int volumeNumber);
    Task<Chapter?> GetByNumberAsync(ProjectId projectId, int volumeNumber, int chapterNumber);
    Task AddAsync(Chapter chapter);
    Task UpdateAsync(Chapter chapter);
}
