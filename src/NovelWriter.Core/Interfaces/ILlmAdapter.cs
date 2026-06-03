using NovelWriter.Core.Dtos;

namespace NovelWriter.Core.Interfaces;

public interface ILlmAdapter
{
    string ModelName { get; }
    int MaxContextTokens { get; }
    int RecommendedOutputTokens { get; }

    Task<string> ChatAsync(
        string systemPrompt,
        string userMessage,
        CancellationToken ct = default);

    Task<string> ChatAsync(
        IEnumerable<ChatMessage> messages,
        CancellationToken ct = default);

    IAsyncEnumerable<string> StreamChatAsync(
        string systemPrompt,
        string userMessage,
        CancellationToken ct = default);
}
