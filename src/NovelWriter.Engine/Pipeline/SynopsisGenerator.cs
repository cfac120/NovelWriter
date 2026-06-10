using System.Text.Json;
using NovelWriter.Core.Dtos;
using NovelWriter.Core.Entities;
using NovelWriter.Core.Interfaces;
using NovelWriter.Engine.Llm;
using Serilog;

namespace NovelWriter.Engine.Pipeline;

/// <summary>
/// 梗概生成器。根据用户填写的题材、标签、故事创意生成五段式故事梗概。
/// 严格要求 LLM 严格按用户给定的题材（genre）和故事创意（storyIdea）生成，
/// 不得擅自切换题材或主题。
/// </summary>
public class SynopsisGenerator
{
    private readonly ILlmAdapter _llm;

    public SynopsisGenerator(ILlmAdapter llm) => _llm = llm;

    /// <summary>
    /// 生成梗概。
    /// </summary>
    /// <param name="title">书名（仅供 LLM 参考，会被允许重命名）</param>
    /// <param name="genre">题材（强制）。如"科幻"/"玄幻"/"现实"等。不可省略。</param>
    /// <param name="tags">标签，逗号分隔。可为空。</param>
    /// <param name="storyIdea">用户原始故事创意。</param>
    /// <param name="targetWordCount">目标字数描述，如"30万"。</param>
    /// <param name="ct"></param>
    public async Task<SynopsisResult> GenerateAsync(
        string title, string genre, string tags, string storyIdea,
        string targetWordCount, CancellationToken ct)
    {
        Log.Information("[SynopsisGen] genre={Genre}, title={Title}, ideaLen={Len}",
            genre, title, storyIdea?.Length ?? 0);

        var systemPrompt = $$"""
            你是网文大纲策划专家。**严格遵守**以下约束：

            1. **题材固定为 "{{{genre}}}"** —— 这是用户钦定的题材类型，整个梗概、主角、冲突、世界观都必须建立在这个题材之上，**不得更换或漂移**。
            2. **故事核心必须围绕用户的"故事创意"展开** —— 用户已经给出了核心构想，你的工作是把它扩展为完整五段式梗概，**不要无视创意去另起炉灶**。
            3. 输出必须是 JSON 格式，不要有 markdown 代码块标记，不要有解释文字。
            4. 语言：中文。

            JSON 字段说明:
            {
              "title": "建议书名(≤20字)",
              "synopsis": "完整梗概(300-500字，含开端/发展/转折/高潮/结局)",
              "main_character": {"name": "主角名", "traits": ["特质1","特质2"]},
              "core_conflict": "核心冲突(一句话)"
            }
            """;

        var userMessage = $"""
            【题材】: {genre}
            【标签】: {tags}
            【目标字数】: {targetWordCount}
            【用户的故事创意】: {storyIdea}

            请严格按照上述题材和故事创意，生成五段式梗概。
            """;

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
