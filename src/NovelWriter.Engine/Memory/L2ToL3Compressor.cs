using NovelWriter.Core.Dtos;
using NovelWriter.Core.Enums;
using NovelWriter.Core.Interfaces;
using NovelWriter.Core.Memory;
using NovelWriter.Core.ValueObjects;
using Serilog;

namespace NovelWriter.Engine.Memory;

/// <summary>
/// L2→L3 卷级压缩器。
/// 触发条件: 当前卷最后一章定稿且 Review 通过。
/// 流程:
///   1. resolved/abandoned 伏笔 → 移入 ForeshadowingArchives
///   2. 完成弧线 → 压缩摘要
///   3. 卷冲突 → VolumeSummaries
///   4. 跨卷 active 伏笔保留
/// </summary>
public class L2ToL3Compressor
{
    private readonly IMemoryRepository _memoryRepo;

    public L2ToL3Compressor(IMemoryRepository memoryRepo)
    {
        _memoryRepo = memoryRepo;
    }

    /// <summary>
    /// 执行卷级压缩。返回压缩报告。
    /// </summary>
    public async Task<VolumeCompressionReport> CompressVolumeAsync(
        ProjectId projectId, int volumeNumber, CancellationToken ct)
    {
        Log.Information("[L2→L3] Compressing Volume {Volume} for Project {Project}", volumeNumber, projectId);

        var allForeshadowings = await _memoryRepo.GetAllForeshadowingsForVolumeAsync(projectId, volumeNumber);
        var arcs = await _memoryRepo.GetArcTrackersAsync(projectId);
        var subplots = await _memoryRepo.GetSubplotTrackersAsync(projectId, volumeNumber);

        var originalTotal = allForeshadowings.Count + arcs.Count + subplots.Count;
        var compressedCount = 0;

        // 1. 已回收/已放弃伏笔 → 归档
        var toArchive = allForeshadowings
            .Where(f => f.Status is ForeshadowingStatus.Resolved or ForeshadowingStatus.Abandoned)
            .ToList();

        foreach (var fs in toArchive)
        {
            await _memoryRepo.AddForeshadowingArchiveAsync(new ForeshadowingArchive
            {
                ForeshadowingId = fs.ForeshadowingId,
                ProjectId = projectId,
                Description = fs.Description,
                Resolution = fs.Status == ForeshadowingStatus.Resolved
                    ? $"在第{fs.ResolvedChapter}章回收" : "已放弃",
                PlantedChapter = fs.PlantedChapter ?? volumeNumber * 100,
                ResolvedChapter = fs.ResolvedChapter ?? 0,
                KeyInsights = $"优先级: {fs.Priority}, 种植方式: {fs.PlantedBy}",
                ArchivedAt = DateTime.UtcNow
            });
            compressedCount++;
            Log.Information("[L2→L3] Archived foreshadowing {FsId}: {Desc}", fs.ForeshadowingId, fs.Description);
        }

        // 2. 卷级摘要
        var volSummary = BuildVolumeSummary(projectId, volumeNumber, allForeshadowings, arcs, subplots);
        await _memoryRepo.AddVolumeSummaryAsync(volSummary);
        compressedCount++;

        // 3. 跨卷 active 伏笔保留在 L2 中（不需要操作）

        var report = new VolumeCompressionReport
        {
            VolumeNumber = volumeNumber,
            OriginalTokenCount = originalTotal * 200, // 粗略估算
            CompressedTokenCount = (originalTotal - compressedCount) * 200
        };

        Log.Information("[L2→L3] Volume {Volume} compressed: {Original} items → {Compressed} items kept, ratio={Ratio:P1}",
            volumeNumber, originalTotal, originalTotal - compressedCount, report.CompressionRatio);

        return report;
    }

    private static VolumeSummary BuildVolumeSummary(
        ProjectId projectId, int volumeNumber,
        IReadOnlyList<Foreshadowing> foregroundings,
        IReadOnlyList<ArcTracker> arcs,
        IReadOnlyList<SubplotTracker> subplots)
    {
        var parts = new List<string>();

        parts.Add($"第{volumeNumber}卷总结");
        parts.Add($"伏笔: {foregroundings.Count(f => f.Status == ForeshadowingStatus.Resolved)}条已回收, " +
                   $"{foregroundings.Count(f => f.Status == ForeshadowingStatus.Abandoned)}条已放弃, " +
                   $"{foregroundings.Count(f => f.Status == ForeshadowingStatus.Active)}条跨卷保留");

        parts.Add($"弧线: {arcs.Count}条");
        parts.Add($"支线: {subplots.Count}条");

        return new VolumeSummary
        {
            ProjectId = projectId,
            VolumeNumber = volumeNumber,
            Summary = string.Join("; ", parts),
            TokenCount = 0
        };
    }
}
