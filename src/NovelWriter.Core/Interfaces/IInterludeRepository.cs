using NovelWriter.Core.Entities;
using NovelWriter.Core.ValueObjects;

namespace NovelWriter.Core.Interfaces;

public interface IInterludeRepository
{
    Task<IReadOnlyList<InterludeEntry>> GetAvailableInterludesAsync();
    Task<InterludeEntry?> GetRandomUnusedInVolumeAsync(ProjectId projectId, int volumeNumber);
    Task LogUsageAsync(string interludeId, ProjectId projectId, int volumeNumber, int chapterNumber);
}
