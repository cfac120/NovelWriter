using System.Text.Json;
using System.Text.Json.Nodes;

namespace NovelWriter.Engine.Llm;

/// <summary>
/// DeepSeek V4 适配器。
/// 端点: https://api.deepseek.com/v1/chat/completions
/// 模型: deepseek-v4-pro (1M 上下文)
/// </summary>
public class DeepSeekAdapter : LlmAdapterBase
{
    public override string ModelName => "deepseek-v4-pro";
    public override int MaxContextTokens => 1_000_000;
    public override int RecommendedOutputTokens => 8_192;

    public DeepSeekAdapter(HttpClient httpClient, string apiKey)
        : base(httpClient, apiKey, "https://api.deepseek.com/v1/chat/completions") { }

    protected override object BuildRequest(string? systemPrompt, string userMessage, bool stream = false)
    {
        var messages = new List<object>();
        if (!string.IsNullOrWhiteSpace(systemPrompt))
            messages.Add(new { role = "system", content = systemPrompt });
        messages.Add(new { role = "user", content = userMessage });

        return new
        {
            model = "deepseek-v4-pro",
            messages,
            stream,
            max_tokens = RecommendedOutputTokens,   // 始终设置保护上限 8192
            temperature = stream ? 0.85 : 0.3
        };
    }

    protected override string ParseResponse(string jsonResponse)
    {
        var node = JsonNode.Parse(jsonResponse)
            ?? throw new InvalidOperationException("Failed to parse DeepSeek response");

        var content = node["choices"]?[0]?["message"]?["content"]?.ToString()
            ?? throw new InvalidOperationException("DeepSeek response missing content");

        return content;
    }

    protected override string? ParseStreamChunk(string data)
    {
        try
        {
            var node = JsonNode.Parse(data);
            var delta = node?["choices"]?[0]?["delta"]?["content"]?.ToString();
            return delta;
        }
        catch
        {
            return null;
        }
    }
}
