using NovelWriter.Core.Dtos;

namespace NovelWriter.Core.Interfaces;

public interface IReviewerAgent
{
    string PersonaName { get; }
    Task<ReviewResult> ReviewAsync(string chapterContent, string outline, CancellationToken ct = default);
}
