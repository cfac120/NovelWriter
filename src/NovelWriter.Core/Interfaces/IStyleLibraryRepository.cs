using NovelWriter.Core.Entities;

namespace NovelWriter.Core.Interfaces;

/// <summary>
/// 风格库仓储。风格库全局共享，不绑定特定项目。
/// </summary>
public interface IStyleLibraryRepository
{
    Task<IReadOnlyList<StyleProfile>> GetAvailableStylesAsync();
    Task<StyleProfile?> GetRandomUnUsedInVolumeAsync(int volumeNumber);
    Task LogUsageAsync(string styleId, int volumeNumber, int chapterNumber);
}
