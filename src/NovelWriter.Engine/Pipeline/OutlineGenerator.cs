using System.Text.Json;
using System.Text.Json.Serialization;
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
        ProjectId projectId, string genre, string synopsis, string coreConflict,
        string mainCharacter, string tags,
        int totalChapters, CancellationToken ct)
    {
        Log.Information("[OutlineGen] Generating outline for {Chapters} chapters", totalChapters);

        var systemPrompt = $$"""
            你是网文大纲策划专家。**严格遵守**：

            1. **题材固定为 "{{{genre}}}"**，所有章节的世界观、事件、人物能力体系必须契合这个题材。**不得漂移到其他题材**。
            2. **必须基于已确认的梗概和核心冲突**展开，**不要重新设定故事方向**。
            3. **输出必须是合法 JSON 数组**——第一项必须是 `{` 开头，`]` 结尾；不得有 markdown 包裹、注释或多余说明。
            4. 共 {{totalChapters}} 章，分卷规则: 每5章1卷。

            每章JSON字段（**所有字符串字段必须是字符串类型，绝对不要输出数组/对象/null 替代字符串**）:
            {
              "chapter_number": 数字（int）,
              "volume_number": 数字（int）,
              "title": "字符串：章节标题(≤15字)",
              "scene_description": "字符串：场景简述(≤50字)",
              "key_events": "字符串：2-3个关键事件，用逗号分隔",
              "character_involvement": "字符串：人物ID列表，用逗号分隔（如 CHAR_001,CHAR_002）"
            }

            第一项必须 chapter_number=1。最后一项 chapter_number={{totalChapters}}。
            """;

        var userMessage = $"""
            【题材】: {genre}
            【已确认的梗概】: {synopsis}
            【核心冲突】: {coreConflict}
            【主角】: {mainCharacter}
            【标签】: {tags}
            """;

        try
        {
            var response = await _llm.ChatAsync(systemPrompt, userMessage, ct);
            var parsed = LlmResponseParser.Parse<List<OutlineDto>>(response);

            if (!parsed.Success || parsed.Value == null || parsed.Value.Count == 0)
            {
                Log.Warning("[OutlineGen] Parse failed or empty: {Errors}; raw={Raw}",
                    string.Join("; ", parsed.Errors), response.Truncate(300));
                return OutlineResult.Fail($"解析大纲失败（{parsed.Errors.FirstOrDefault() ?? "未知错误"}）。请重试，或检查 LLM 是否返回了合法 JSON 数组。");
            }

            var outlines = parsed.Value.Select(d => new Outline
            {
                ProjectId = projectId,
                ChapterNumber = d.chapter_number,
                VolumeNumber = d.volume_number <= 0 ? 1 : d.volume_number,
                SceneDescription = !string.IsNullOrWhiteSpace(d.title)
                    ? d.title
                    : (!string.IsNullOrWhiteSpace(d.scene_description) ? d.scene_description : $"第{d.chapter_number}章"),
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

    /// <summary>
    /// DTO 用宽松接收：所有非必需字符串字段都用 <see cref="JsonElement"/> 接收，
    /// 然后在映射时安全 ToString() —— 容错 LLM 输出 null / 数字 / 数组等。
    /// </summary>
    private class OutlineDto
    {
        [JsonPropertyName("chapter_number")]
        [JsonConverter(typeof(LooseIntConverter))]
        public int chapter_number { get; set; } = 0;

        [JsonPropertyName("volume_number")]
        [JsonConverter(typeof(LooseIntConverter))]
        public int volume_number { get; set; } = 0;

        [JsonPropertyName("title")]
        [JsonConverter(typeof(LooseStringConverter))]
        public string? title { get; set; }

        [JsonPropertyName("scene_description")]
        [JsonConverter(typeof(LooseStringConverter))]
        public string? scene_description { get; set; }

        [JsonPropertyName("key_events")]
        [JsonConverter(typeof(LooseStringConverter))]
        public string? key_events { get; set; }

        [JsonPropertyName("character_involvement")]
        [JsonConverter(typeof(LooseStringConverter))]
        public string? character_involvement { get; set; }
    }
}

public class OutlineResult
{
    public List<Outline> Outlines { get; init; } = [];
    public string? Error { get; init; }
    public bool Success => Error == null;
    public static OutlineResult Fail(string error) => new() { Error = error };
}
