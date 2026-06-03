using NovelWriter.Core.Entities;
using NovelWriter.Core.ValueObjects;

namespace NovelWriter.Core.Interfaces;

public interface IStyleLibraryRepository
{
    Task<IReadOnlyList<StyleProfile>> GetAvailableStylesAsync();
    Task<StyleProfile?> GetRandomUnUsedInVolumeAsync(ProjectId projectId, int volumeNumber);
    Task LogUsageAsync(string styleId, ProjectId projectId, int volumeNumber, int chapterNumber);
}
