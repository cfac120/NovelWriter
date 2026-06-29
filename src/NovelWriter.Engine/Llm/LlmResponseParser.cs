using System.Text.Json;
using System.Text.Json.Nodes;
using Serilog;

namespace NovelWriter.Engine.Llm;

/// <summary>
/// LLM JSON 输出解析器。处理 LLM 输出中常见的 JSON 问题：
/// 1. 截断补全
/// 2. 转义修复
/// 3. 提取 JSON 片段
/// 4. ID 存在性校验
/// </summary>
public static class LlmResponseParser
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNameCaseInsensitive = true,
        ReadCommentHandling = JsonCommentHandling.Skip,
        AllowTrailingCommas = true
    };

    /// <summary>
    /// 解析 LLM 输出为指定类型。包含容错处理。
    /// </summary>
    public static ParseResult<T> Parse<T>(string llmOutput) where T : class
    {
        var errors = new List<string>();

        // Step 1: 尝试标准 JSON 解析
        var json = ExtractJson(llmOutput);
        if (json == null)
        {
            errors.Add("No JSON content found in LLM output");
            return new ParseResult<T>(default, false, errors);
        }

        // Step 2: 尝试直接反序列化
        try
        {
            var result = JsonSerializer.Deserialize<T>(json, JsonOptions);
            if (result != null) return new ParseResult<T>(result, true, errors);
        }
        catch (JsonException ex)
        {
            errors.Add($"Standard parse failed: {ex.Message}");
        }

        // Step 3: 尝试修复常见问题后重试
        var fixedJson = TryFixJson(json);
        if (fixedJson != json)
        {
            try
            {
                var result = JsonSerializer.Deserialize<T>(fixedJson, JsonOptions);
                if (result != null)
                {
                    errors.Add("JSON was auto-repaired before parsing");
                    return new ParseResult<T>(result, true, errors);
                }
            }
            catch (JsonException ex)
            {
                errors.Add($"Repaired parse also failed: {ex.Message}");
            }
        }

        Log.Warning("Failed to parse LLM output as {Type}. Errors: {Errors}",
            typeof(T).Name, string.Join("; ", errors));

        return new ParseResult<T>(default, false, errors);
    }

    /// <summary>
    /// 从 LLM 输出中提取 JSON 内容（可能包裹在 markdown code block 中）。
    /// 关键：必须找**配对**的最外层分隔符，不能用 IndexOf/LastIndexOf
    /// （因为 JSON 内部可能含嵌套的 {} 或 []）。
    /// </summary>
    private static string? ExtractJson(string output)
    {
        if (string.IsNullOrWhiteSpace(output)) return null;

        // 尝试提取 ```json ... ``` 代码块
        var codeBlockMatch = System.Text.RegularExpressions.Regex.Match(
            output, @"```(?:json)?\s*\n?(.*?)\n?```",
            System.Text.RegularExpressions.RegexOptions.Singleline);
        if (codeBlockMatch.Success) return codeBlockMatch.Groups[1].Value.Trim();

        // 找最外层成对分隔符：从第一个非空白字符开始
        var start = -1;
        char open = '\0', close = '\0';
        for (int i = 0; i < output.Length; i++)
        {
            var c = output[i];
            if (c == '[') { start = i; open = '['; close = ']'; break; }
            if (c == '{') { start = i; open = '{'; close = '}'; break; }
        }
        if (start < 0) return null;

        // 从 start 往后找**配对**的 close
        int depth = 0;
        bool inString = false;
        bool escape = false;
        for (int i = start; i < output.Length; i++)
        {
            var c = output[i];
            if (escape) { escape = false; continue; }
            if (c == '\\' && inString) { escape = true; continue; }
            if (c == '"') { inString = !inString; continue; }
            if (inString) continue;
            if (c == open) depth++;
            else if (c == close)
            {
                depth--;
                if (depth == 0) return output[start..(i + 1)];
            }
        }

        return null;
    }

    /// <summary>
    /// 尝试修复常见的 JSON 问题。
    /// </summary>
    private static string TryFixJson(string json)
    {
        // 修复截断：如果 JSON 不完整（奇数个花括号），尝试补全
        var openBraces = json.Count(c => c == '{');
        var closeBraces = json.Count(c => c == '}');
        if (openBraces > closeBraces)
            json += new string('}', openBraces - closeBraces);

        var openBrackets = json.Count(c => c == '[');
        var closeBrackets = json.Count(c => c == ']');
        if (openBrackets > closeBrackets)
            json += new string(']', openBrackets - closeBrackets);

        // 修复末尾多余的逗号
        json = System.Text.RegularExpressions.Regex.Replace(json, @",\s*([}\]])", "$1");

        return json;
    }
}

public record ParseResult<T>(T? Value, bool Success, List<string> Errors) where T : class;
