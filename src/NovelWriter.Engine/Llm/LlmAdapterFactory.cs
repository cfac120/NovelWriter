using Microsoft.Extensions.DependencyInjection;
using NovelWriter.Core.Interfaces;

namespace NovelWriter.Engine.Llm;

/// <summary>
/// LLM 适配器工厂。根据模型名称创建对应的适配器实例。
/// 支持 DeepSeek / Qwen / Kimi / 通用 OpenAI 兼容端点。
/// 所有适配器共享同一个 <see cref="LlmRuntimeConfig"/>，UI 端保存配置时
/// 调用 config.Update() 即可让所有已构造的适配器生效。
/// </summary>
public class LlmAdapterFactory
{
    private readonly IServiceProvider _serviceProvider;
    private readonly LlmDegradationPolicy _degradationPolicy;
    private readonly LlmRuntimeConfig _config;
    private readonly Dictionary<string, string> _apiKeys;

    public LlmAdapterFactory(
        IServiceProvider serviceProvider,
        LlmDegradationPolicy degradationPolicy,
        LlmRuntimeConfig config,
        Dictionary<string, string> apiKeys)
    {
        _serviceProvider = serviceProvider;
        _degradationPolicy = degradationPolicy;
        _config = config;
        _apiKeys = apiKeys;
    }

    /// <summary>
    /// 根据模型名称创建对应的 LLM 适配器。
    /// </summary>
    public ILlmAdapter Create(string modelName)
    {
        // 把 key 同步到共享 config（兼容旧调用方）
        if (_apiKeys.TryGetValue("deepseek", out var dsKey) && !string.IsNullOrWhiteSpace(dsKey))
            _config.Update(apiKey: dsKey);
        else if (_apiKeys.TryGetValue("qwen", out var qwKey) && !string.IsNullOrWhiteSpace(qwKey))
            _config.Update(apiKey: qwKey);
        else if (_apiKeys.TryGetValue("kimi", out var kmKey) && !string.IsNullOrWhiteSpace(kmKey))
            _config.Update(apiKey: kmKey);

        return modelName switch
        {
            "deepseek-v4-pro" => CreateDeepSeekAdapter(),
            "qwen-plus" => CreateQwenAdapter(modelName),
            "qwen-max" => CreateQwenAdapter(modelName),
            "moonshot-v1-8k" => CreateKimiAdapter(modelName),
            "moonshot-v1-32k" => CreateKimiAdapter(modelName),
            "moonshot-v1-128k" => CreateKimiAdapter(modelName),
            _ => CreateGenericAdapter()
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

    private ILlmAdapter CreateDeepSeekAdapter()
    {
        _config.Update(endpoint: "https://api.deepseek.com/v1/chat/completions", model: "deepseek-v4-pro");
        var httpClientFactory = _serviceProvider.GetRequiredService<IHttpClientFactory>();
        var httpClient = httpClientFactory.CreateClient("DeepSeek");
        return new DeepSeekAdapter(httpClient, _config);
    }

    private ILlmAdapter CreateQwenAdapter(string modelName)
    {
        _config.Update(endpoint: "https://dashscope.aliyuncs.com/compatible-mode/v1/chat/completions", model: modelName);
        var httpClientFactory = _serviceProvider.GetRequiredService<IHttpClientFactory>();
        var httpClient = httpClientFactory.CreateClient("Qwen");
        return new QwenAdapter(httpClient, _config, modelName);
    }

    private ILlmAdapter CreateKimiAdapter(string modelName)
    {
        _config.Update(endpoint: "https://api.moonshot.cn/v1/chat/completions", model: modelName);
        var httpClientFactory = _serviceProvider.GetRequiredService<IHttpClientFactory>();
        var httpClient = httpClientFactory.CreateClient("Kimi");
        return new KimiAdapter(httpClient, _config, modelName);
    }

    private ILlmAdapter CreateGenericAdapter()
    {
        var httpClientFactory = _serviceProvider.GetRequiredService<IHttpClientFactory>();
        var httpClient = httpClientFactory.CreateClient("Generic");
        return new GenericOpenAiAdapter(httpClient, _config);
    }
}
