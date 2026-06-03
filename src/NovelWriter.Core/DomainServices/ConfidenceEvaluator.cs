using NovelWriter.Core.Enums;

namespace NovelWriter.Core.DomainServices;

public static class ConfidenceEvaluator
{
    public static Confidence FromScore(double score) => score >= 0.7 ? Confidence.High : Confidence.Low;

    public static bool ShouldAutoConfirm(Confidence confidence, string itemType) =>
        confidence == Confidence.High && itemType is "NewForeshadowing" or "SubplotUpdate";

    public static bool NeedsUserConfirmation(Confidence confidence, string itemType) =>
        !ShouldAutoConfirm(confidence, itemType);
}
