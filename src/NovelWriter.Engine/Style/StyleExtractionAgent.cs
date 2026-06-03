using System.Text.Json;
using NovelWriter.Core.Entities;
using NovelWriter.Core.Interfaces;
using NovelWriter.Engine.Llm;
using Serilog;

namespace NovelWriter.Engine.Style;

/// <summary>
/// 风格提取 Agent。
/// 从短篇文本中提取结构化写作风格档案（5 个维度）。
/// </summary>
public class StyleExtractionAgent
{
    private readonly ILlmAdapter _llm;
    private readonly IStyleLibraryRepository _repo;

    public StyleExtractionAgent(ILlmAdapter llm, IStyleLibraryRepository repo)
    {
        _llm = llm;
        _repo = repo;
    }

    /// <summary>
    /// 从给定文本中提取风格档案。
    /// </summary>
    /// <param name="storyText">短篇全文（≤5000字）</param>
    /// <param name="sourceTitle">来源标题</param>
    /// <param name="sourceAuthor">来源作者</param>
    /// <returns>提取的 StyleProfile 或 null（解析失败时）</returns>
    public async Task<StyleProfile?> ExtractAsync(
        string storyText, string sourceTitle, string sourceAuthor, CancellationToken ct)
    {
        Log.Information("[StyleExtraction] Extracting style from '{Title}' by {Author}",
            sourceTitle, sourceAuthor);

        var systemPrompt = """
            你是一个文学风格分析专家。请分析以下文本的写作风格，提取五个维度的特征。
            输出 JSON 格式，每个维度用中文描述。

            五个维度:
            1. sentence_patterns (句式特征): 句长偏好、句式复杂度、排比/对偶使用频率
            2. lexical_preferences (词汇偏好): 用词倾向、高频词类型、成语/典故使用
            3. rhetorical_habits (修辞习惯): 比喻/拟人/夸张/白描等手法的使用倾向
            4. narrative_distance (叙事距离): 全知视角/限知视角/客观记录，心理描写深度
            5. paragraph_rhythm (段落节奏): 段落长度、场景切换频率、描写与对话比例

            输出格式:
            {
              "sentence_patterns": "一段中文描述（50字内）",
              "lexical_preferences": "一段中文描述（50字内）",
              "rhetorical_habits": "一段中文描述（50字内）",
              "narrative_distance": "一段中文描述（50字内）",
              "paragraph_rhythm": "一段中文描述（50字内）"
            }
            """;

        var userMessage = $"""
            来源: 《{sourceTitle}》作者: {sourceAuthor}

            文本:
            {storyText.Truncate(5000)}
            """;

        try
        {
            var response = await _llm.ChatAsync(systemPrompt, userMessage, ct);
            var parsed = LlmResponseParser.Parse<StyleProfileDto>(response);

            if (!parsed.Success || parsed.Value == null)
            {
                Log.Warning("[StyleExtraction] Failed to parse style extraction response");
                return null;
            }

            var dto = parsed.Value;
            var profileJson = JsonSerializer.Serialize(new
            {
                sentence_patterns = dto.sentence_patterns,
                lexical_preferences = dto.lexical_preferences,
                rhetorical_habits = dto.rhetorical_habits,
                narrative_distance = dto.narrative_distance,
                paragraph_rhythm = dto.paragraph_rhythm
            });

            var tags = ExtractTags(dto);

            var profile = new StyleProfile
            {
                Id = $"STYLE_{Guid.NewGuid():N}"[..12],
                SourceTitle = sourceTitle,
                SourceAuthor = sourceAuthor,
                SourceType = "manual",
                SourceWordCount = storyText.Length,
                ProfileJson = profileJson,
                Tags = JsonSerializer.Serialize(tags),
                UsageCount = 0,
                CreatedAt = DateTime.UtcNow
            };

            Log.Information("[StyleExtraction] Successfully extracted style '{Id}'", profile.Id);
            return profile;
        }
        catch (Exception ex)
        {
            Log.Error(ex, "[StyleExtraction] LLM call failed for '{Title}'", sourceTitle);
            return null;
        }
    }

    /// <summary>
    /// 批量提取多个来源的风格档案。
    /// </summary>
    public async Task<IReadOnlyList<StyleProfile>> ExtractBatchAsync(
        IReadOnlyList<(string Text, string Title, string Author)> sources, CancellationToken ct)
    {
        var results = new List<StyleProfile>();
        foreach (var (text, title, author) in sources)
        {
            var profile = await ExtractAsync(text, title, author, ct);
            if (profile != null) results.Add(profile);
        }
        return results;
    }

    private static List<string> ExtractTags(StyleProfileDto dto)
    {
        var tags = new List<string>();
        var allText = $"{dto.sentence_patterns} {dto.lexical_preferences} {dto.rhetorical_habits} {dto.narrative_distance} {dto.paragraph_rhythm}";

        if (allText.Contains("短句") || allText.Contains("短小")) tags.Add("短句风格");
        if (allText.Contains("长句") || allText.Contains("复杂句")) tags.Add("长句风格");
        if (allText.Contains("文言") || allText.Contains("古典") || allText.Contains("成语")) tags.Add("文言倾向");
        if (allText.Contains("白描") || allText.Contains("客观")) tags.Add("白描风格");
        if (allText.Contains("心理") || allText.Contains("内心")) tags.Add("心理描写");
        if (allText.Contains("全知") || allText.Contains("上帝视角")) tags.Add("全知视角");
        if (allText.Contains("限知") || allText.Contains("第一人称")) tags.Add("限知视角");
        if (allText.Contains("对话") && allText.Contains("多")) tags.Add("对话密集");
        if (allText.Contains("描写") && allText.Contains("多")) tags.Add("描写丰富");

        return tags;
    }

    private record StyleProfileDto(
        string? sentence_patterns,
        string? lexical_preferences,
        string? rhetorical_habits,
        string? narrative_distance,
        string? paragraph_rhythm);
}
