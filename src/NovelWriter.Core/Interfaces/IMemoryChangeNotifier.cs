using NovelWriter.Core.Dtos;

namespace NovelWriter.Core.Interfaces;

public interface IMemoryChangeNotifier
{
    Task NotifyChangesAsync(MemoryExtractionResult extraction, IReadOnlyList<ConfirmationDecision> decisions);
}
