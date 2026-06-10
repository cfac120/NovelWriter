using System.Runtime.CompilerServices;
using System.Text;
using System.Text.Json;
using NovelWriter.Core.Dtos;
using NovelWriter.Core.Exceptions;
using NovelWriter.Core.Interfaces;
using Polly;
using Polly.Retry;
using Serilog;

namespace NovelWriter.Engine.Llm;

/// <summary>
/// LLM 适配器抽象基类，封装重试/超时/降级/日志/流式输出共用逻辑。
/// 子类仅需覆写 BuildRequest 和 ParseResponse。
/// </summary>
public abstract class LlmAdapterBase : ILlmAdapter, IDisposable
{
    protected readonly HttpClient HttpClient;
    protected readonly LlmRuntimeConfig Config;
    protected readonly ResiliencePipeline<HttpResponseMessage> ResiliencePipeline;

    // 速率限制：最少间隔2秒，防止快速连续调用耗尽配额
    private static readonly SemaphoreSlim RateGate = new(1, 1);
    private static DateTime _lastCallTime = DateTime.MinValue;
    private static readonly TimeSpan MinCallInterval = TimeSpan.FromSeconds(2);

    /// <summary>
    /// 构造时 ApiKey 可以为空（启动时未配置场景）。请求时再校验。
    /// 所有运行时配置通过 <see cref="LlmRuntimeConfig"/> 引用，UI 保存配置时
    /// 调用 <c>Config.Update(key, model, url)</c>，下次请求立即生效。
    /// </summary>
    protected LlmAdapterBase(HttpClient httpClient, LlmRuntimeConfig config)
    {
        HttpClient = httpClient ?? throw new ArgumentNullException(nameof(httpClient));
        Config = config ?? throw new ArgumentNullException(nameof(config));

        ResiliencePipeline = new ResiliencePipelineBuilder<HttpResponseMessage>()
            .AddRetry(new RetryStrategyOptions<HttpResponseMessage>
            {
                MaxRetryAttempts = 3,
                Delay = TimeSpan.FromSeconds(1),
                BackoffType = DelayBackoffType.Exponential,
                UseJitter = true,
                ShouldHandle = new PredicateBuilder<HttpResponseMessage>()
                    .Handle<HttpRequestException>()
                    .HandleResult(r => (int)r.StatusCode >= 500 || r.StatusCode == System.Net.HttpStatusCode.RequestTimeout),
                OnRetry = args =>
                {
                    Log.Warning("LLM retry {Attempt}/3 after {Delay}ms for {Model}. Reason: {Reason}",
                        args.AttemptNumber + 1, args.RetryDelay.TotalMilliseconds, Config.Model,
                        args.Outcome.Exception?.Message ?? args.Outcome.Result?.StatusCode.ToString());
                    return default;
                }
            })
            .AddTimeout(TimeSpan.FromMinutes(5))
            .Build();
    }

    /// <summary>
    /// 子类可覆写：当前请求使用的模型名（默认从 Config 读）
    /// </summary>
    protected virtual string ActiveModel => Config.Model;
    protected virtual string ActiveApiKey => Config.ApiKey;
    protected virtual string ActiveEndpoint => Config.Endpoint;

    /// <summary>
    /// 守卫：确保两次调用之间有最小间隔，防止 token 滥用。
    /// </summary>
    private static async Task WaitRateGateAsync(CancellationToken ct)
    {
        await RateGate.WaitAsync(ct);
        try
        {
            var elapsed = DateTime.UtcNow - _lastCallTime;
            if (elapsed < MinCallInterval)
                await Task.Delay(MinCallInterval - elapsed, ct);
            _lastCallTime = DateTime.UtcNow;
        }
        finally
        {
            RateGate.Release();
        }
    }

    public void Dispose()
    {
        HttpClient.Dispose();
        GC.SuppressFinalize(this);
    }

    public abstract string ModelName { get; }
    public abstract int MaxContextTokens { get; }
    public abstract int RecommendedOutputTokens { get; }

    /// <summary>
    /// 非流式单轮对话。用于记忆提取、评审等需要完整 JSON 输出的场景。
    /// </summary>
    public async Task<string> ChatAsync(string systemPrompt, string userMessage, CancellationToken ct = default)
    {
        await WaitRateGateAsync(ct);
        var apiKey = ActiveApiKey;
        if (string.IsNullOrWhiteSpace(apiKey))
            throw new InvalidOperationException("请先在左下角 ▼ 配置 LLM API Key");
        var endpoint = ActiveEndpoint;
        if (string.IsNullOrWhiteSpace(endpoint))
            throw new InvalidOperationException("请先在左下角 ▼ 配置 LLM API 端点 URL");
        var requestBody = BuildRequest(systemPrompt, userMessage, stream: false);
        var response = await SendRequestAsync(requestBody, apiKey, endpoint, ct);
        return ParseResponse(response);
    }

    /// <summary>
    /// 非流式多轮对话。取最后一条 user + 合并前面为 context，转调单轮。
    /// </summary>
    public async Task<string> ChatAsync(IEnumerable<ChatMessage> messages, CancellationToken ct = default)
    {
        var msgList = messages.ToList();
        if (msgList.Count == 0) throw new ArgumentException("Messages cannot be empty", nameof(messages));

        var lastUserMsg = msgList.Last(m => m.Role == "user");
        var contextBuilder = new StringBuilder();
        foreach (var msg in msgList.Take(msgList.Count - 1))
        {
            contextBuilder.AppendLine($"[{msg.Role}]: {msg.Content}");
        }
        var combinedUserMsg = contextBuilder.Length > 0
            ? $"对话上下文:\n{contextBuilder}\n\n当前问题: {lastUserMsg.Content}"
            : lastUserMsg.Content;

        return await ChatAsync(null!, combinedUserMsg, ct);
    }

    /// <summary>
    /// 流式单轮对话。用于章节写作等长输出场景，UI 可逐步渲染文本。
    /// 内置重试（最多1次额外尝试）、速率限制和超时保护。
    /// </summary>
    public async IAsyncEnumerable<string> StreamChatAsync(
        string systemPrompt, string userMessage,
        [EnumeratorCancellation] CancellationToken ct = default)
    {
        await WaitRateGateAsync(ct);

        var apiKey = ActiveApiKey;
        if (string.IsNullOrWhiteSpace(apiKey))
            throw new InvalidOperationException("请先在左下角 ▼ 配置 LLM API Key");
        var endpoint = ActiveEndpoint;
        if (string.IsNullOrWhiteSpace(endpoint))
            throw new InvalidOperationException("请先在左下角 ▼ 配置 LLM API 端点 URL");

        var requestBody = BuildRequest(systemPrompt, userMessage, stream: true);
        var json = JsonSerializer.Serialize(requestBody);

        const int maxAttempts = 2;
        for (int attempt = 0; attempt < maxAttempts; attempt++)
        {
            HttpResponseMessage response;
            try
            {
                using var request = new HttpRequestMessage(HttpMethod.Post, endpoint);
                request.Headers.Authorization = new System.Net.Http.Headers.AuthenticationHeaderValue("Bearer", apiKey);
                request.Content = new StringContent(json, Encoding.UTF8, "application/json");

                using var timeoutCts = new CancellationTokenSource(TimeSpan.FromMinutes(5));
                using var linkedCts = CancellationTokenSource.CreateLinkedTokenSource(ct, timeoutCts.Token);
                response = await HttpClient.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, linkedCts.Token);
            }
            catch (OperationCanceledException) when (!ct.IsCancellationRequested)
            {
                if (attempt < maxAttempts - 1) { await Task.Delay(2000, ct); continue; }
                throw new TimeoutException("流式请求超时（5分钟），已重试全部失败");
            }
            catch (HttpRequestException) when (attempt < maxAttempts - 1)
            {
                await Task.Delay(2000, ct);
                continue;
            }

            if (!response.IsSuccessStatusCode && attempt < maxAttempts - 1)
            {
                response.Dispose();
                await Task.Delay(2000, ct);
                continue;
            }
            response.EnsureSuccessStatusCode();

            using var stream = await response.Content.ReadAsStreamAsync(ct);
            using var reader = new StreamReader(stream);

            string? line;
            while ((line = await reader.ReadLineAsync(ct)) != null)
            {
                if (string.IsNullOrEmpty(line)) continue;
                if (!line.StartsWith("data: ")) continue;
                var data = line["data: ".Length..];
                if (data == "[DONE]") yield break;

                var chunk = ParseStreamChunk(data);
                if (chunk != null) yield return chunk;
            }
            yield break;
        }
    }

    // === 子类覆写 ===

    protected abstract object BuildRequest(string? systemPrompt, string userMessage, bool stream = false);
    protected abstract string ParseResponse(string jsonResponse);
    protected abstract string? ParseStreamChunk(string data);

    // === 内部方法 ===

    private async Task<string> SendRequestAsync(object requestBody, string apiKey, string endpoint, CancellationToken ct)
    {
        var json = JsonSerializer.Serialize(requestBody);
        var startTime = DateTime.UtcNow;

        try
        {
            var outcome = await ResiliencePipeline.ExecuteAsync(async token =>
            {
                using var request = new HttpRequestMessage(HttpMethod.Post, endpoint);
                request.Headers.Authorization = new System.Net.Http.Headers.AuthenticationHeaderValue("Bearer", apiKey);
                request.Content = new StringContent(json, Encoding.UTF8, "application/json");

                return await HttpClient.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, token);
            }, ct);

            var responseBody = await outcome.Content.ReadAsStringAsync(ct);
            var elapsed = (DateTime.UtcNow - startTime).TotalMilliseconds;

            Log.Information("LLM call completed: Model={Model}, Duration={Duration}ms, Status={Status}",
                Config.Model, elapsed, outcome.StatusCode);

            return responseBody;
        }
        catch (Exception ex) when (ex is not LlmUnavailableException)
        {
            var elapsed = (DateTime.UtcNow - startTime).TotalMilliseconds;
            Log.Error(ex, "LLM call failed after retries: Model={Model}, Duration={Duration}ms", Config.Model, elapsed);
            throw;
        }
    }
}
