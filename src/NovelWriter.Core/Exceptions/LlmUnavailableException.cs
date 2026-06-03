namespace NovelWriter.Core.Exceptions;

public class LlmUnavailableException : Exception
{
    public string ModelName { get; }

    public LlmUnavailableException(string modelName, string? message = null, Exception? inner = null)
        : base(message ?? $"LLM adapter '{modelName}' is unavailable", inner)
    {
        ModelName = modelName;
    }
}
