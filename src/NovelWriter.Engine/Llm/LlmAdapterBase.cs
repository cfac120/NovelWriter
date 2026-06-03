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
public abstract class LlmAdapterBase : ILlmAdapter
{
    protected readonly HttpClient HttpClient;
    protected readonly string ApiKey;
    protected readonly string ChatEndpoint;
    protected readonly ResiliencePipeline<HttpResponseMessage> ResiliencePipeline;

    protected LlmAdapterBase(HttpClient httpClient, string apiKey, string chatEndpoint)
    {
        HttpClient = httpClient ?? throw new ArgumentNullException(nameof(httpClient));
        ApiKey = !string.IsNullOrWhiteSpace(apiKey) ? apiKey : throw new ArgumentException("API Key cannot be empty", nameof(apiKey));
        ChatEndpoint = chatEndpoint ?? throw new ArgumentNullException(nameof(chatEndpoint));

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
                        args.AttemptNumber + 1, args.RetryDelay.TotalMilliseconds, ModelName,
                        args.Outcome.Exception?.Message ?? args.Outcome.Result?.StatusCode.ToString());
                    return default;
                }
            })
            .AddTimeout(TimeSpan.FromMinutes(5))
            .Build();
    }

    public abstract string ModelName { get; }
    public abstract int MaxContextTokens { get; }
    public abstract int RecommendedOutputTokens { get; }

    /// <summary>
    /// 非流式单轮对话。用于记忆提取、评审等需要完整 JSON 输出的场景。
    /// </summary>
    public async Task<string> ChatAsync(string systemPrompt, string userMessage, CancellationToken ct = default)
    {
        var requestBody = BuildRequest(systemPrompt, userMessage, stream: false);
        var response = await SendRequestAsync(requestBody, ct);
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
    /// </summary>
    public async IAsyncEnumerable<string> StreamChatAsync(
        string systemPrompt, string userMessage,
        [EnumeratorCancellation] CancellationToken ct = default)
    {
        var requestBody = BuildRequest(systemPrompt, userMessage, stream: true);
        var json = JsonSerializer.Serialize(requestBody);
        using var request = new HttpRequestMessage(HttpMethod.Post, ChatEndpoint);
        request.Headers.Authorization = new System.Net.Http.Headers.AuthenticationHeaderValue("Bearer", ApiKey);
        request.Content = new StringContent(json, Encoding.UTF8, "application/json");

        using var response = await HttpClient.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, ct);
        response.EnsureSuccessStatusCode();

        using var stream = await response.Content.ReadAsStreamAsync(ct);
        using var reader = new StreamReader(stream);

        string? line;
        while ((line = await reader.ReadLineAsync(ct)) != null)
        {
            if (string.IsNullOrEmpty(line)) continue;

            // SSE format: "data: {json}" or "data: [DONE]"
            if (!line.StartsWith("data: ")) continue;
            var data = line["data: ".Length..];
            if (data == "[DONE]") break;

            var chunk = ParseStreamChunk(data);
            if (chunk != null) yield return chunk;
        }
    }

    // === 子类覆写 ===

    protected abstract object BuildRequest(string? systemPrompt, string userMessage, bool stream = false);
    protected abstract string ParseResponse(string jsonResponse);
    protected abstract string? ParseStreamChunk(string data);

    // === 内部方法 ===

    private async Task<string> SendRequestAsync(object requestBody, CancellationToken ct)
    {
        var json = JsonSerializer.Serialize(requestBody);
        var startTime = DateTime.UtcNow;

        try
        {
            var outcome = await ResiliencePipeline.ExecuteAsync(async token =>
            {
                using var request = new HttpRequestMessage(HttpMethod.Post, ChatEndpoint);
                request.Headers.Authorization = new System.Net.Http.Headers.AuthenticationHeaderValue("Bearer", ApiKey);
                request.Content = new StringContent(json, Encoding.UTF8, "application/json");

                return await HttpClient.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, token);
            }, ct);

            var responseBody = await outcome.Content.ReadAsStringAsync(ct);
            var elapsed = (DateTime.UtcNow - startTime).TotalMilliseconds;

            Log.Information("LLM call completed: Model={Model}, Duration={Duration}ms, Status={Status}",
                ModelName, elapsed, outcome.StatusCode);

            return responseBody;
        }
        catch (Exception ex) when (ex is not LlmUnavailableException)
        {
            var elapsed = (DateTime.UtcNow - startTime).TotalMilliseconds;
            Log.Error(ex, "LLM call failed after retries: Model={Model}, Duration={Duration}ms", ModelName, elapsed);
            throw;
        }
    }
}
