using System.Text.Json;
using NovelWriter.Core.Dtos;
using NovelWriter.Core.Entities;
using NovelWriter.Core.Interfaces;
using NovelWriter.Engine.Llm;
using Serilog;

namespace NovelWriter.Engine.Pipeline;

/// <summary>
/// 梗概生成器。根据题材偏好生成五段式故事梗概。
/// </summary>
public class SynopsisGenerator
{
    private readonly ILlmAdapter _llm;

    public SynopsisGenerator(ILlmAdapter llm) => _llm = llm;

    public async Task<SynopsisResult> GenerateAsync(
        string genre, string tags, string targetWordCount, CancellationToken ct)
    {
        Log.Information("[SynopsisGen] Generating synopsis for genre={Genre}", genre);

        var systemPrompt = """
            你是网文大纲策划专家。根据题材信息生成一个300-500字的五段式故事梗概。
            输出JSON格式:
            {
              "title": "建议书名(≤20字)",
              "synopsis": "完整梗概(300-500字，含开端/发展/转折/高潮/结局)",
              "main_character": {"name": "主角名", "traits": ["特质1","特质2"]},
              "core_conflict": "核心冲突(一句话)"
            }
            """;

        var userMessage = $"题材: {genre}\n标签: {tags}\n目标字数: {targetWordCount}";

        try
        {
            var response = await _llm.ChatAsync(systemPrompt, userMessage, ct);
            var parsed = LlmResponseParser.Parse<SynopsisDto>(response);

            if (!parsed.Success || parsed.Value == null)
            {
                Log.Warning("[SynopsisGen] Parse failed: {Errors}", string.Join("; ", parsed.Errors));
                return SynopsisResult.Fail("解析LLM输出失败，请重试");
            }

            return new SynopsisResult
            {
                Title = parsed.Value.title?.Trim() ?? "",
                Synopsis = parsed.Value.synopsis?.Trim() ?? "",
                MainCharacterName = parsed.Value.main_character?.name?.Trim() ?? "",
                MainCharacterTraits = parsed.Value.main_character?.traits ?? [],
                CoreConflict = parsed.Value.core_conflict?.Trim() ?? ""
            };
        }
        catch (Exception ex)
        {
            Log.Error(ex, "[SynopsisGen] Failed");
            return SynopsisResult.Fail($"生成失败: {ex.Message}");
        }
    }

    private record SynopsisDto(string? title, string? synopsis,
        MainCharDto? main_character, string? core_conflict);
    private record MainCharDto(string? name, List<string>? traits);
}

public class SynopsisResult
{
    public string Title { get; init; } = "";
    public string Synopsis { get; init; } = "";
    public string MainCharacterName { get; init; } = "";
    public List<string> MainCharacterTraits { get; init; } = [];
    public string CoreConflict { get; init; } = "";
    public string? Error { get; init; }
    public bool Success => Error == null;

    public static SynopsisResult Fail(string error) => new() { Error = error };
}
