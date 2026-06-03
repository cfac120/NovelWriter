using System.Text.Json;
using System.Text.Json.Nodes;

namespace NovelWriter.Engine.Llm;

/// <summary>
/// Moonshot (Kimi) 适配器。
/// 端点: https://api.moonshot.cn/v1/chat/completions
/// 模型: moonshot-v1-8k / 32k / 128k
/// </summary>
public class KimiAdapter : LlmAdapterBase
{
    private readonly string _modelName;

    public override string ModelName => _modelName;
    public override int MaxContextTokens => _modelName switch
    {
        "moonshot-v1-8k" => 8_192,
        "moonshot-v1-32k" => 32_768,
        "moonshot-v1-128k" => 131_072,
        _ => 8_192
    };
    public override int RecommendedOutputTokens => 4_096;

    public KimiAdapter(HttpClient httpClient, string apiKey, string modelName = "moonshot-v1-128k")
        : base(httpClient, apiKey, "https://api.moonshot.cn/v1/chat/completions")
    {
        _modelName = modelName;
    }

    protected override object BuildRequest(string? systemPrompt, string userMessage, bool stream = false)
    {
        var messages = new List<object>();
        if (!string.IsNullOrWhiteSpace(systemPrompt))
            messages.Add(new { role = "system", content = systemPrompt });
        messages.Add(new { role = "user", content = userMessage });

        return new
        {
            model = _modelName,
            messages,
            stream,
            temperature = stream ? 0.85 : 0.3
        };
    }

    protected override string ParseResponse(string jsonResponse)
    {
        var node = JsonNode.Parse(jsonResponse)
            ?? throw new InvalidOperationException("Failed to parse Kimi response");

        var content = node["choices"]?[0]?["message"]?["content"]?.ToString()
            ?? throw new InvalidOperationException("Kimi response missing content");

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
