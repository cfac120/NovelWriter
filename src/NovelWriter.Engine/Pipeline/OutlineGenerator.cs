using System.Text.Json;
using NovelWriter.Core.Entities;
using NovelWriter.Core.Interfaces;
using NovelWriter.Core.ValueObjects;
using NovelWriter.Engine.Llm;
using Serilog;

namespace NovelWriter.Engine.Pipeline;

/// <summary>
/// 大纲生成器。根据梗概生成分卷分章大纲。
/// </summary>
public class OutlineGenerator
{
    private readonly ILlmAdapter _llm;

    public OutlineGenerator(ILlmAdapter llm) => _llm = llm;

    public async Task<OutlineResult> GenerateAsync(
        ProjectId projectId, string synopsis, string coreConflict,
        string mainCharacter, string tags,
        int totalChapters, CancellationToken ct)
    {
        Log.Information("[OutlineGen] Generating outline for {Chapters} chapters", totalChapters);

        var systemPrompt = @$"你是网文大纲策划专家。为故事规划分卷分章大纲。
共需要{totalChapters}章，分卷规则: 每5章1卷。

每章输出JSON格式:
{{
  ""chapter_number"": 数字,
  ""volume_number"": 数字(每5章递增),
  ""title"": ""章节标题(≤15字)"",
  ""scene_description"": ""本章场景简述(≤50字)"",
  ""key_events"": ""2-3个关键事件"",
  ""character_involvement"": ""涉及人物ID列表(如CHAR_001)""
}}

输出完整JSON数组。第一项必须chapter_number=1。";

        var userMessage = $"""
            梗概: {synopsis}
            核心冲突: {coreConflict}
            主角: {mainCharacter}
            标签: {tags}
            """;

        try
        {
            var response = await _llm.ChatAsync(systemPrompt, userMessage, ct);
            var parsed = LlmResponseParser.Parse<List<OutlineDto>>(response);

            if (!parsed.Success || parsed.Value == null || parsed.Value.Count == 0)
            {
                Log.Warning("[OutlineGen] Parse failed or empty: {Errors}",
                    string.Join("; ", parsed.Errors));
                return OutlineResult.Fail("解析大纲失败，请重试");
            }

            var outlines = parsed.Value.Select(d => new Outline
            {
                ProjectId = projectId,
                ChapterNumber = d.chapter_number,
                VolumeNumber = d.volume_number,
                SceneDescription = d.title ?? d.scene_description ?? $"第{d.chapter_number}章",
                KeyEvents = d.key_events ?? "",
                CharacterInvolvement = d.character_involvement ?? "CHAR_001"
            }).ToList();

            Log.Information("[OutlineGen] Generated {Count} chapter outlines", outlines.Count);
            return new OutlineResult { Outlines = outlines };
        }
        catch (Exception ex)
        {
            Log.Error(ex, "[OutlineGen] Failed");
            return OutlineResult.Fail($"生成失败: {ex.Message}");
        }
    }

    private record OutlineDto(int chapter_number, int volume_number, string? title,
        string? scene_description, string? key_events, string? character_involvement);
}

public class OutlineResult
{
    public List<Outline> Outlines { get; init; } = [];
    public string? Error { get; init; }
    public bool Success => Error == null;
    public static OutlineResult Fail(string error) => new() { Error = error };
}
