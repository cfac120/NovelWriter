using NovelWriter.Core.Entities;
using NovelWriter.Core.Interfaces;
using NovelWriter.Core.ValueObjects;
using Serilog;

namespace NovelWriter.Engine.Style;

/// <summary>
/// 插曲注入器。
/// 每章写作前随机选取插曲条目，调用 LLM 改编为 ≤100 字闲笔，
/// 追加到 User Message 末尾。
/// </summary>
public class InterludeInjector
{
    private readonly IInterludeRepository _repo;
    private readonly ILlmAdapter _adaptationLlm;
    private readonly string _modelName;

    public InterludeInjector(IInterludeRepository repo, ILlmAdapter adaptationLlm, string modelName)
    {
        _repo = repo;
        _adaptationLlm = adaptationLlm;
        _modelName = modelName;
    }

    /// <summary>
    /// 随机选择插曲并改编。卷内不重复。返回改编后的闲笔文本。
    /// 若无可用插曲或改编失败，返回 null（静默跳过）。
    /// </summary>
    public async Task<(InterludeEntry? Entry, string? AdaptedText)> PrepareInterludeAsync(
        ProjectId projectId, int volumeNumber, int chapterNumber, string chapterContext, CancellationToken ct)
    {
        var entry = await _repo.GetRandomUnusedInVolumeAsync(projectId, volumeNumber);

        if (entry == null)
        {
            Log.Debug("[InterludeInjector] No available interlude for Volume {Vol} Chapter {Chap}",
                volumeNumber, chapterNumber);
            return (null, null);
        }

        var adaptedText = await AdaptInterludeAsync(entry, chapterContext, ct);

        if (adaptedText == null)
        {
            Log.Warning("[InterludeInjector] Adaptation failed for interlude '{Id}', skipping", entry.Id);
            return (null, null);
        }

        await _repo.LogUsageAsync(entry.Id, projectId, volumeNumber, chapterNumber);

        Log.Information("[InterludeInjector] Injected interlude '{Id}' ({Source}) for Chapter {Chap}",
            entry.Id, entry.Source, chapterNumber);

        return (entry, adaptedText);
    }

    private async Task<string?> AdaptInterludeAsync(
        InterludeEntry entry, string chapterContext, CancellationToken ct)
    {
        var systemPrompt = """
            将历史典故改编为修仙故事闲笔。硬性要求: 输出严格≤100字，超过立截。直接输出改编文本，不解释。
            """;

        var userMessage = $"""
            典故: {entry.CoreFact}
            叙事钩子: {entry.NarrativeHook}
            章节语境: {chapterContext.Truncate(200)}

            【改编≤100字，超了截断，直接写】:
            """;

        try
        {
            var response = await _adaptationLlm.ChatAsync(systemPrompt, userMessage, ct);
            var trimmed = response.Trim();

            // 硬截断: 超过100字直接切
            if (trimmed.Length > 100)
            {
                Log.Debug("[InterludeInjector] Adapted text too long ({Len} chars), hard-truncating to 100", trimmed.Length);
                trimmed = trimmed[..100];
            }

            return trimmed;
        }
        catch (Exception ex)
        {
            Log.Error(ex, "[InterludeInjector] LLM adaptation call failed for interlude '{Id}'", entry.Id);
            return null;
        }
    }
}
