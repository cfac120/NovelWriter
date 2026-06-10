namespace NovelWriter.Engine.Llm;

/// <summary>
/// 线程安全的 LLM 运行时配置。供所有 <see cref="ILlmAdapter"/> 共享，
/// UI 端保存配置时调用 <see cref="Update"/> 即可让所有已构造的适配器
/// 在下一次请求时使用最新 Key/Model/Endpoint，无需重建 DI 容器。
/// </summary>
public sealed class LlmRuntimeConfig
{
    private readonly object _lock = new();
    private string _apiKey;
    private string _model;
    private string _endpoint;

    public LlmRuntimeConfig(string apiKey = "", string model = "", string endpoint = "")
    {
        _apiKey = apiKey ?? "";
        _model = model ?? "";
        _endpoint = endpoint ?? "";
    }

    public string ApiKey
    {
        get { lock (_lock) return _apiKey; }
    }

    public string Model
    {
        get { lock (_lock) return _model; }
    }

    public string Endpoint
    {
        get { lock (_lock) return _endpoint; }
    }

    /// <summary>
    /// 原子地更新三项配置。空字符串会保留旧值。
    /// </summary>
    public void Update(string? apiKey = null, string? model = null, string? endpoint = null)
    {
        lock (_lock)
        {
            if (apiKey != null) _apiKey = apiKey;
            if (model != null) _model = model;
            if (endpoint != null) _endpoint = endpoint;
        }
    }

    public bool HasKey => !string.IsNullOrWhiteSpace(ApiKey);
    public bool HasEndpoint => !string.IsNullOrWhiteSpace(Endpoint);
}
