using System.Text.Json.Nodes;

namespace NovelWriter.Engine.Llm;

/// <summary>
/// 通用 OpenAI 兼容适配器。支持任意兼容 OpenAI API 的端点。
/// 构造时 ApiKey 可以为空（启动时未配置场景），每次请求从 <see cref="LlmRuntimeConfig"/> 实时读取。
/// </summary>
public class GenericOpenAiAdapter : LlmAdapterBase
{
    private readonly string _initialModel;

    public override string ModelName => string.IsNullOrEmpty(Config.Model) ? _initialModel : Config.Model;
    public override int MaxContextTokens => 128_000;
    public override int RecommendedOutputTokens => 8_192;

    public GenericOpenAiAdapter(HttpClient httpClient, LlmRuntimeConfig config)
        : base(httpClient, config)
    {
        _initialModel = config.Model;
    }

    protected override object BuildRequest(string? systemPrompt, string userMessage, bool stream = false)
    {
        var messages = new List<object>();
        if (!string.IsNullOrWhiteSpace(systemPrompt))
            messages.Add(new { role = "system", content = systemPrompt });
        messages.Add(new { role = "user", content = userMessage });

        return new
        {
            model = Config.Model,
            messages,
            temperature = 0.7,
            max_tokens = RecommendedOutputTokens,
            stream
        };
    }

    protected override string ParseResponse(string jsonResponse)
    {
        var node = JsonNode.Parse(jsonResponse)
            ?? throw new InvalidOperationException("Failed to parse response");
        return node["choices"]?[0]?["message"]?["content"]?.ToString()
            ?? throw new InvalidOperationException("Response missing content");
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
