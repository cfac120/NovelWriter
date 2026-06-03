using NovelWriter.Core.Dtos;
using NovelWriter.Core.Enums;
using NovelWriter.Core.Interfaces;
using NovelWriter.Core.Memory;
using NovelWriter.Core.ValueObjects;
using Serilog;

namespace NovelWriter.Engine.Memory;

/// <summary>
/// L2 更新器 — 负责将 MemoryExtractionResult 中的变更写入 L2 实体。
/// 伏笔回收、新伏笔、弧线进度、支线状态更新。
/// </summary>
public class L2Updater
{
    private readonly IMemoryRepository _memoryRepo;
    private const int DanglingThreshold = 10;

    public L2Updater(IMemoryRepository memoryRepo)
    {
        _memoryRepo = memoryRepo;
    }

    /// <summary>
    /// 根据确认后的决策写入 L2 变更。
    /// </summary>
    public async Task ApplyChangesAsync(
        MemoryExtractionResult extraction,
        IReadOnlyList<ConfirmationDecision> decisions,
        ProjectId projectId,
        int volumeNumber,
        int chapterNumber,
        CancellationToken ct)
    {
        var approvedIds = decisions.Where(d => d.Approved).Select(d => d.ItemId).ToHashSet();

        // Task B: 伏笔回收
        foreach (var resolution in extraction.ForeshadowingResolutions)
        {
            if (resolution.Confidence == Confidence.High ||
                approvedIds.Contains(GetItemId(extraction.NeedsConfirmationItems, resolution)))
            {
                // 查找匹配的伏笔
                var activeFs = await _memoryRepo.GetActiveForeshadowingsAsync(projectId, volumeNumber);
                var match = activeFs.FirstOrDefault(f =>
                    f.ForeshadowingId.ToString() == resolution.ForeshadowingId);
                if (match != null)
                {
                    match.Status = ForeshadowingStatus.Resolved;
                    match.ResolvedChapter = chapterNumber;
                    await _memoryRepo.UpdateForeshadowingAsync(match);
                    Log.Information("[L2] Resolved foreshadowing {FsId} in Chapter {Chapter}",
                        resolution.ForeshadowingId, chapterNumber);
                }
            }
        }

        // Task C: 新伏笔（仅批准后写入）
        var newFsCandidates = extraction.NewForeshadowings
            .Select((fs, i) => (fs, itemId: extraction.NeedsConfirmationItems
                .FirstOrDefault(c => c.Type == "NewForeshadowing" && c.Payload == fs)?.Id))
            .Where(x => x.itemId != null && approvedIds.Contains(x.itemId!));

        foreach (var (candidate, _) in newFsCandidates)
        {
            var maxId = (await _memoryRepo.GetActiveForeshadowingsAsync(projectId, int.MaxValue))
                .Select(f => f.ForeshadowingId.Number).DefaultIfEmpty(0).Max();

            var newFs = new Foreshadowing
            {
                ForeshadowingId = new ForeshadowingId(maxId + 1),
                ProjectId = projectId,
                VolumeNumber = volumeNumber,
                Description = candidate.Description,
                Status = ForeshadowingStatus.Active,
                Priority = candidate.Priority,
                PlantedBy = PlantedBy.AutoDetected,
                PlantedChapter = chapterNumber,
                RelatedCharacterIds = candidate.RelatedCharacterIds
            };
            await _memoryRepo.AddForeshadowingAsync(newFs);
            Log.Information("[L2] New auto-detected foreshadowing {FsId}: {Desc}",
                newFs.ForeshadowingId, candidate.Description.Truncate(50));
        }

        // Task D: 弧线里程碑
        foreach (var arcUpdate in extraction.ArcUpdates)
        {
            var arcs = await _memoryRepo.GetArcTrackersAsync(projectId);
            var match = arcs.FirstOrDefault(a => a.ArcId.ToString() == arcUpdate.ArcId);
            if (match != null)
            {
                match.Milestones = UpdateMilestonesJson(match.Milestones, arcUpdate.MilestoneName, arcUpdate.Status);
                match.UpdatedAt = DateTime.UtcNow;
                await _memoryRepo.UpdateArcTrackerAsync(match);
                Log.Information("[L2] Updated arc {ArcId} milestone '{Milestone}' → {Status}",
                    arcUpdate.ArcId, arcUpdate.MilestoneName, arcUpdate.Status);
            }
        }

        // Task E: 支线状态
        foreach (var subUpdate in extraction.SubplotUpdates)
        {
            var subplots = await _memoryRepo.GetSubplotTrackersAsync(projectId, volumeNumber);
            var match = subplots.FirstOrDefault(s =>
                string.Equals(s.Name, subUpdate.SubplotName, StringComparison.OrdinalIgnoreCase));
            if (match != null)
            {
                match.WasMentionedThisChapter = subUpdate.WasMentioned;
                match.DanglingChapterCount = subUpdate.WasMentioned ? 0 : match.DanglingChapterCount + 1;
                match.UpdatedAt = DateTime.UtcNow;
                await _memoryRepo.UpdateSubplotTrackerAsync(match);

                if (match.DanglingChapterCount > DanglingThreshold)
                {
                    Log.Warning("[L2] Subplot '{Name}' dangling for {Count} chapters, needs author attention",
                        match.Name, match.DanglingChapterCount);
                }
            }
        }
    }

    private static string UpdateMilestonesJson(string currentJson, string milestoneName, ArcMilestoneStatus status)
    {
        // 简单处理：追加新里程碑状态
        if (string.IsNullOrWhiteSpace(currentJson)) return $"[{{\"name\":\"{milestoneName}\",\"status\":\"{status}\"}}]";

        try
        {
            var milestones = System.Text.Json.JsonSerializer.Deserialize<List<MilestoneEntry>>(currentJson) ?? [];
            var existing = milestones.FirstOrDefault(m =>
                string.Equals(m.name, milestoneName, StringComparison.OrdinalIgnoreCase));
            if (existing != null)
                existing.status = status.ToString().ToLowerInvariant();
            else
                milestones.Add(new MilestoneEntry { name = milestoneName, status = status.ToString().ToLowerInvariant() });

            return System.Text.Json.JsonSerializer.Serialize(milestones);
        }
        catch
        {
            return currentJson;
        }
    }

    private class MilestoneEntry
    {
        public string name { get; set; } = "";
        public string status { get; set; } = "";
    }

    private static string GetItemId(List<ConfirmationItem> items, object? payload)
    {
        return items.FirstOrDefault(i => i.Payload == payload)?.Id ?? "";
    }
}
