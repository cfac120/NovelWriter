using Microsoft.EntityFrameworkCore;
using NovelWriter.Core.Entities;
using NovelWriter.Core.Interfaces;
using NovelWriter.Core.ValueObjects;

namespace NovelWriter.Storage.Repositories;

public class ProjectRepository : IProjectRepository
{
    private readonly NovelWriterDbContext _db;

    public ProjectRepository(NovelWriterDbContext db) => _db = db;

    public async Task<Project?> GetByIdAsync(ProjectId id) =>
        await _db.Projects.FindAsync(id);

    public async Task<IReadOnlyList<Project>> GetAllAsync() =>
        await _db.Projects.ToListAsync();

    public async Task AddAsync(Project project)
    {
        await _db.Projects.AddAsync(project);
        await _db.SaveChangesAsync();
    }

    public async Task UpdateAsync(Project project)
    {
        _db.Projects.Update(project);
        await _db.SaveChangesAsync();
    }
}
