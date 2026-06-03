using NovelWriter.Core.Dtos;
using NovelWriter.Core.Interfaces;
using Serilog;

namespace NovelWriter.Engine.Review;

/// <summary>
/// 子Agent 评审编排器。并行调用多个 Persona 评审。
/// 至少 2 个 Persona 成功返回才计算综合评分。
/// </summary>
public class ReviewOrchestrator
{
    private readonly ILlmAdapter _reviewLlm;
    private readonly List<PersonaDefinition> _personas;
    private const double PassThreshold = 7.0;
    private const int DefaultPersonaCount = 4;

    public ReviewOrchestrator(ILlmAdapter reviewLlm, List<PersonaDefinition>? personas = null)
    {
        _reviewLlm = reviewLlm;
        _personas = personas ?? GetDefaultPersonas();
    }

    /// <summary>
    /// 并行评审章节。所有 Persona 通过 Task.WhenAll 同时调用。
    /// </summary>
    public async Task<AggregatedReview> ReviewChapterAsync(
        string chapterContent, string outline, string writingSystemPrompt,
        int personaCount = DefaultPersonaCount, CancellationToken ct = default)
    {
        var selected = _personas.Take(personaCount).ToList();
        if (selected.Count < 2)
            throw new InvalidOperationException("Need at least 2 personas for review");

        var tasks = selected.Select(p => ReviewWithPersonaAsync(
            chapterContent, outline, writingSystemPrompt, p, ct));

        var results = await Task.WhenAll(tasks);
        var successful = results.Where(r => r != null).Cast<ReviewResult>().ToList();

        if (successful.Count < 2)
        {
            Log.Error("Review failed: only {Count}/{Total} personas succeeded", successful.Count, selected.Count);
            throw new Core.Exceptions.LlmUnavailableException(
                "Insufficient review results: need at least 2 successful reviews");
        }

        return Aggregate(successful);
    }

    private async Task<ReviewResult?> ReviewWithPersonaAsync(
        string chapterContent, string outline, string writingSystemPrompt,
        PersonaDefinition persona, CancellationToken ct)
    {
        try
        {
            var systemPrompt = BuildReviewSystemPrompt(persona);
            var userMessage = BuildReviewUserMessage(chapterContent, outline, writingSystemPrompt);

            var llmResponse = await _reviewLlm.ChatAsync(systemPrompt, userMessage, ct);
            var parseResult = Llm.LlmResponseParser.Parse<ReviewJsonDto>(llmResponse);

            if (parseResult.Success && parseResult.Value != null)
            {
                return new ReviewResult
                {
                    PersonaName = persona.Name,
                    Score = parseResult.Value.overall,
                    Feedback = string.Join("; ", parseResult.Value.weaknesses ?? []),
                    Suggestions = string.Join("; ", parseResult.Value.suggestions ?? [])
                };
            }

            Log.Warning("Failed to parse review from {Persona}: {Errors}",
                persona.Name, string.Join("; ", parseResult.Errors));
            return null;
        }
        catch (Exception ex)
        {
            Log.Warning(ex, "Review failed for persona {Persona}", persona.Name);
            return null;
        }
    }

    private static AggregatedReview Aggregate(List<ReviewResult> results)
    {
        var avgScore = results.Average(r => r.Score);
        return new AggregatedReview
        {
            AverageScore = avgScore,
            IsPassing = avgScore >= PassThreshold,
            Reviews = results,
            Consensus = results.Count(r => r.Score >= PassThreshold) >= results.Count / 2
                ? "多数评审者认为本章合格" : "多数评审者认为本章需要润色"
        };
    }

    private static string BuildReviewSystemPrompt(PersonaDefinition persona)
    {
        return
"你是一位" + persona.Role + "，正在评审一部网文小说的章节。\n" +
"\n" +
"你的评审偏好: " + persona.Preferences + "\n" +
"你的批判风格: " + persona.CritiqueStyle + "\n" +
"\n" +
"请按以下 JSON 格式输出评审结果:\n" +
"{\n" +
"  \"overall\": 1-10,\n" +
"  \"strengths\": [\"优点1\", \"优点2\"],\n" +
"  \"weaknesses\": [\"缺点1\", \"缺点2\"],\n" +
"  \"suggestions\": [\"建议1\", \"建议2\"],\n" +
"  \"flagged\": [{\"type\": \"continuity_error\", \"detail\": \"描述\"}]\n" +
"}\n" +
"\n" +
"要求:\n" +
"- overall 为 1-10 分综合评分\n" +
"- 严格按 JSON 格式输出\n";
    }

    private static string BuildReviewUserMessage(string chapterContent, string outline, string writingSystemPrompt)
    {
        return $"""
## 本章大纲
{outline}

## 写作时的系统约束
{writingSystemPrompt.Truncate(500)}

## 章节正文（前2000字用于评审）
{chapterContent.Truncate(2000)}
""";
    }

    // === 默认 Persona ===

    public static List<PersonaDefinition> GetDefaultPersonas() =>
    [
        new("爽文党", "爽文读者", "高爽点密度,快速节奏", "关注爽感足够否"),
        new("逻辑党", "逻辑型读者", "设定一致性,伏笔回收", "挑剔逻辑漏洞,行为合理性"),
        new("情感党", "情感型读者", "人物关系,情感共鸣", "关注人物成长和关系发展"),
        new("老书虫", "资深读者", "创新度,文笔质量", "识别套路,评判文学性"),
        new("追更党", "追更型读者", "钩子,悬念", "关注章末钩子是否够吸引人"),
        new("考据党", "考据型读者", "细节真实,文化考究", "挑细节bug和文化错误")
    ];

    public record PersonaDefinition(string Name, string Role, string Preferences, string CritiqueStyle);

    private record ReviewJsonDto(
        double overall,
        List<string>? strengths,
        List<string>? weaknesses,
        List<string>? suggestions,
        List<FlaggedDto>? flagged);

    private record FlaggedDto(string type, string detail);
}

internal static class StringTruncate
{
    public static string Truncate(this string value, int maxLength) =>
        string.IsNullOrEmpty(value) || value.Length <= maxLength ? value : value[..maxLength] + "...";
}
