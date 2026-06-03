using NovelWriter.Core.Dtos;
using NovelWriter.Core.Enums;
using NovelWriter.Core.Interfaces;
using NovelWriter.Core.Memory;
using NovelWriter.Core.ValueObjects;
using Serilog;

namespace NovelWriter.Engine.Memory;

/// <summary>
/// 记忆变更验证器。在写入前对提取结果做 ID 存在性校验和合法性检查。
/// </summary>
public class MemoryChangeValidator
{
    private readonly IMemoryRepository _memoryRepo;

    public MemoryChangeValidator(IMemoryRepository memoryRepo)
    {
        _memoryRepo = memoryRepo;
    }

    /// <summary>
    /// 验证提取结果中的 ID 引用是否有效，过滤无效引用。
    /// </summary>
    public async Task<MemoryExtractionResult> ValidateAndFilterAsync(
        MemoryExtractionResult extraction,
        ProjectId projectId,
        int volumeNumber,
        CancellationToken ct)
    {
        var activeFs = await _memoryRepo.GetActiveForeshadowingsAsync(projectId, volumeNumber);
        var arcs = await _memoryRepo.GetArcTrackersAsync(projectId);
        var subplots = await _memoryRepo.GetSubplotTrackersAsync(projectId, volumeNumber);
        var chars = await _memoryRepo.GetCharacterProfilesAsync(projectId);
        var settings = await _memoryRepo.GetWorldSettingsAsync(projectId);

        var validFsIds = activeFs.Select(f => f.ForeshadowingId.ToString()).ToHashSet();
        var validArcIds = arcs.Select(a => a.ArcId.ToString()).ToHashSet();
        var validCharIds = chars.Select(c => c.CharacterId.ToString()).ToHashSet();

        // 过滤无效伏笔回收引用
        extraction.ForeshadowingResolutions.RemoveAll(r =>
        {
            if (!validFsIds.Contains(r.ForeshadowingId))
            {
                Log.Warning("[Validator] Removing invalid foreshadowing reference: {Id}", r.ForeshadowingId);
                return true;
            }
            return false;
        });

        // 过滤无效弧线引用
        extraction.ArcUpdates.RemoveAll(a =>
        {
            if (!validArcIds.Contains(a.ArcId))
            {
                Log.Warning("[Validator] Removing invalid arc reference: {Id}", a.ArcId);
                return true;
            }
            return false;
        });

        // 过滤无效 L3 变更引用
        extraction.L3ChangeProposals.RemoveAll(c =>
        {
            if (!validCharIds.Contains(c.TargetId))
            {
                Log.Warning("[Validator] Removing invalid L3 change target: {Id}", c.TargetId);
                return true;
            }
            return false;
        });

        return extraction;
    }
}
