using Microsoft.Extensions.DependencyInjection;
using NovelWriter.Core.Interfaces;

namespace NovelWriter.Engine.Llm;

/// <summary>
/// LLM 适配器工厂。根据模型名称创建对应的适配器实例。
/// 支持 DeepSeek / Qwen / Kimi 三个服务。
/// </summary>
public class LlmAdapterFactory
{
    private readonly IServiceProvider _serviceProvider;
    private readonly LlmDegradationPolicy _degradationPolicy;
    private readonly Dictionary<string, string> _apiKeys;

    public LlmAdapterFactory(
        IServiceProvider serviceProvider,
        LlmDegradationPolicy degradationPolicy,
        Dictionary<string, string> apiKeys)
    {
        _serviceProvider = serviceProvider;
        _degradationPolicy = degradationPolicy;
        _apiKeys = apiKeys;
    }

    /// <summary>
    /// 根据模型名称创建对应的 LLM 适配器。
    /// 如果模型不在降级链中（如 qwen-plus），直接创建。
    /// 如果模型在降级链中，使用降级策略获取当前可用模型。
    /// </summary>
    public ILlmAdapter Create(string modelName)
    {
        return modelName switch
        {
            "deepseek-v4-pro" => CreateDeepSeekAdapter(modelName),
            "qwen-plus" => CreateQwenAdapter(modelName),
            "qwen-max" => CreateQwenAdapter(modelName),
            "moonshot-v1-8k" => CreateKimiAdapter(modelName),
            "moonshot-v1-32k" => CreateKimiAdapter(modelName),
            "moonshot-v1-128k" => CreateKimiAdapter(modelName),
            _ => throw new ArgumentException($"Unknown model: {modelName}", nameof(modelName))
        };
    }

    /// <summary>
    /// 使用降级策略获取当前可用模型，并创建对应适配器。
    /// </summary>
    public ILlmAdapter CreateWithDegradation()
    {
        var activeModel = _degradationPolicy.GetActiveModel();
        return Create(activeModel);
    }

    private ILlmAdapter CreateDeepSeekAdapter(string modelName)
    {
        var apiKey = GetApiKey("deepseek");
        var httpClientFactory = _serviceProvider.GetRequiredService<IHttpClientFactory>();
        var httpClient = httpClientFactory.CreateClient("DeepSeek");
        return new DeepSeekAdapter(httpClient, apiKey);
    }

    private ILlmAdapter CreateQwenAdapter(string modelName)
    {
        var apiKey = GetApiKey("qwen");
        var httpClientFactory = _serviceProvider.GetRequiredService<IHttpClientFactory>();
        var httpClient = httpClientFactory.CreateClient("Qwen");
        return new QwenAdapter(httpClient, apiKey, modelName);
    }

    private ILlmAdapter CreateKimiAdapter(string modelName)
    {
        var apiKey = GetApiKey("kimi");
        var httpClientFactory = _serviceProvider.GetRequiredService<IHttpClientFactory>();
        var httpClient = httpClientFactory.CreateClient("Kimi");
        return new KimiAdapter(httpClient, apiKey, modelName);
    }

    private string GetApiKey(string service)
    {
        if (!_apiKeys.TryGetValue(service, out var apiKey) || string.IsNullOrWhiteSpace(apiKey))
            throw new InvalidOperationException($"API key for '{service}' is not configured");
        return apiKey;
    }
}
