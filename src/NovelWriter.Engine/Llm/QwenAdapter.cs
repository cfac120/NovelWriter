using System.Text.Json;
using System.Text.Json.Nodes;

namespace NovelWriter.Engine.Llm;

/// <summary>
/// 通义千问适配器。
/// 端点: https://dashscope.aliyuncs.com/compatible-mode/v1/chat/completions
/// 模型: qwen-plus / qwen-max
/// </summary>
public class QwenAdapter : LlmAdapterBase
{
    private readonly string _modelName;

    public override string ModelName => _modelName;
    public override int MaxContextTokens => _modelName == "qwen-max" ? 32_768 : 131_072;
    public override int RecommendedOutputTokens => 8_192;

    public QwenAdapter(HttpClient httpClient, LlmRuntimeConfig config, string modelName = "qwen-plus")
        : base(httpClient, config)
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
            max_tokens = stream ? RecommendedOutputTokens : (int?)null,
            temperature = stream ? 0.85 : 0.3
        };
    }

    protected override string ParseResponse(string jsonResponse)
    {
        var node = JsonNode.Parse(jsonResponse)
            ?? throw new InvalidOperationException("Failed to parse Qwen response");

        return node["choices"]?[0]?["message"]?["content"]?.ToString()
            ?? throw new InvalidOperationException("Qwen response missing content");
    }

    protected override string? ParseStreamChunk(string data)
    {
        try
        {
            var node = JsonNode.Parse(data);
            return node?["choices"]?[0]?["delta"]?["content"]?.ToString();
        }
        catch { return null; }
    }
}
