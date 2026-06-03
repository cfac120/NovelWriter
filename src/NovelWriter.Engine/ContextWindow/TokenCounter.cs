using System.Collections.Concurrent;
using Serilog;

namespace NovelWriter.Engine.ContextWindow;

/// <summary>
/// Token 计数器。采用"调用前估算 + 调用后校准"策略。
/// 中文 ≈ 1.5 char/token，英文 ≈ 4 char/token。
/// 每次 LLM 调用后通过 Calibrate 方法用实际 usage 数据动态更新校准系数（EMA）。
/// </summary>
public class TokenCounter
{
    private readonly ConcurrentDictionary<string, double> _calibratedRatios = new();

    /// <summary>
    /// 估算文本的 token 数量，使用模型特定校准系数。
    /// </summary>
    public int Estimate(string text, string modelName)
    {
        if (string.IsNullOrEmpty(text)) return 0;

        var ratio = _calibratedRatios.GetValueOrDefault(modelName, 1.5);
        var chineseChars = CountChineseChars(text);
        var englishChars = text.Length - chineseChars;
        return (int)(chineseChars / ratio + englishChars / 4.0);
    }

    /// <summary>
    /// 每次 LLM 调用后，用 response 中的 usage.prompt_tokens 校准系数。
    /// 使用指数移动平均 (EMA) 平滑波动。
    /// </summary>
    public void Calibrate(string text, string modelName, int actualTokens)
    {
        if (string.IsNullOrEmpty(text) || actualTokens <= 0) return;

        var chineseChars = CountChineseChars(text);
        if (chineseChars < 10) return; // 中文太少，校准无意义

        var actualRatio = (double)chineseChars / (actualTokens * (1.0 - (text.Length - chineseChars) / (4.0 * actualTokens)));
        if (!double.IsFinite(actualRatio) || actualRatio <= 0) return;

        _calibratedRatios[modelName] = _calibratedRatios.TryGetValue(modelName, out var old)
            ? old * 0.7 + actualRatio * 0.3
            : actualRatio;

        Log.Debug("Token ratio calibrated: Model={Model}, OldRatio={Old:F3}, NewRatio={New:F3}, ActualTokens={Actual}",
            modelName, _calibratedRatios.GetValueOrDefault(modelName, 1.5), _calibratedRatios[modelName], actualTokens);
    }

    /// <summary>
    /// 预算安全边际: 始终预留 15%。
    /// </summary>
    public bool IsWithinBudget(int estimatedTokens, int maxBudget)
        => estimatedTokens <= maxBudget * 0.85;

    private static int CountChineseChars(string text)
    {
        var count = 0;
        foreach (var c in text)
        {
            if (c >= 0x4E00 && c <= 0x9FFF ||  // CJK Unified Ideographs
                c >= 0x3400 && c <= 0x4DBF ||  // CJK Extension A
                c >= 0x3000 && c <= 0x303F ||  // CJK Symbols and Punctuation
                c >= 0xFF00 && c <= 0xFFEF)     // Fullwidth Forms
                count++;
        }
        return count;
    }
}
