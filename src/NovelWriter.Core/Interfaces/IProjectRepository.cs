using NovelWriter.Core.Entities;
using NovelWriter.Core.ValueObjects;

namespace NovelWriter.Core.Interfaces;

public interface IProjectRepository
{
    Task<Project?> GetByIdAsync(ProjectId id);
    Task<IReadOnlyList<Project>> GetAllAsync();
    Task AddAsync(Project project);
    Task UpdateAsync(Project project);
}
