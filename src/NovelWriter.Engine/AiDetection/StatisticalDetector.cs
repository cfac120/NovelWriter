using NovelWriter.Core.Dtos;
using NovelWriter.Core.Enums;
using Serilog;

namespace NovelWriter.Engine.AiDetection;

/// <summary>
/// 统计特征 AI 检测器。纯规则检测（无 LLM 调用）。
/// 4 个维度: 句式均匀性、词汇多样性(TTR)、情绪节奏、段落结构。
/// </summary>
public class StatisticalDetector
{
    private const double SentenceLengthVarianceThreshold = 0.3;
    private const double TtrThreshold = 0.5;
    private const double EmotionVarianceThreshold = 0.15;
    private const double ParagraphPatternThreshold = 0.3;

    /// <summary>
    /// 对章节正文执行统计特征检测。
    /// </summary>
    public DetectionReport Analyze(string chapterContent)
    {
        if (string.IsNullOrWhiteSpace(chapterContent))
            return new DetectionReport { OverallRisk = AiRiskLevel.Low };

        var details = new List<DetectionDetail>();

        // 1. 句式均匀性
        var sentences = SplitSentences(chapterContent);
        if (sentences.Count > 5)
        {
            var lengths = sentences.Select(s => s.Length).ToList();
            var avg = lengths.Average();
            var variance = lengths.Average(l => Math.Pow(l - avg, 2));
            var cv = Math.Sqrt(variance) / avg; // 变异系数

            double score;
            if (cv < SentenceLengthVarianceThreshold) score = 0.8;
            else if (cv < 0.5) score = 0.5;
            else score = 0.2;

            details.Add(new DetectionDetail
            {
                Method = "句式均匀性",
                Score = score,
                Explanation = $"句长变异系数: {cv:F2} (越低越像AI)"
            });
        }

        // 2. 词汇多样性 (TTR)
        if (chapterContent.Length > 50)
        {
            var chars = chapterContent.Where(c => c >= 0x4E00 && c <= 0x9FFF).ToList();
            var uniqueCount = chars.Distinct().Count();
            var ttr = chars.Count > 0 ? (double)uniqueCount / chars.Count : 1;

            double score;
            if (ttr < TtrThreshold) score = 0.7;
            else if (ttr < 0.7) score = 0.4;
            else score = 0.1;

            details.Add(new DetectionDetail
            {
                Method = "词汇多样性(TTR)",
                Score = score,
                Explanation = $"TTR: {ttr:F2} (越低越像AI)"
            });
        }

        // 3. 情绪节奏简化检测
        if (sentences.Count > 10)
        {
            var emotionScores = sentences.Select(EstimateEmotionScore).ToList();
            var avgEmotion = emotionScores.Average();
            var emoVariance = emotionScores.Average(e => Math.Pow(e - avgEmotion, 2));

            double score;
            if (emoVariance < EmotionVarianceThreshold) score = 0.6;
            else if (emoVariance < 0.3) score = 0.4;
            else score = 0.2;

            details.Add(new DetectionDetail
            {
                Method = "情绪节奏",
                Score = score,
                Explanation = $"情绪方差: {emoVariance:F3} (越低情绪越单调)"
            });
        }

        // 4. 段落首句模式
        var paragraphs = chapterContent.Split("\n\n", StringSplitOptions.RemoveEmptyEntries);
        if (paragraphs.Length > 3)
        {
            var firstSentences = paragraphs.Select(p => p.Split('。', '.')[0]).Where(s => s.Length > 2).ToList();
            var patterns = firstSentences.GroupBy(s => s.Length / 10)
                .Select(g => (double)g.Count() / firstSentences.Count)
                .Max();

            double score;
            if (patterns > ParagraphPatternThreshold) score = 0.7;
            else score = 0.3;

            details.Add(new DetectionDetail
            {
                Method = "段落首句模式",
                Score = score,
                Explanation = $"首句长度聚类度: {patterns:F2} (越高越像AI)"
            });
        }

        var overallScore = details.Count > 0 ? details.Average(d => d.Score) : 0;
        var risk = overallScore switch
        {
            < 0.3 => AiRiskLevel.Low,
            < 0.5 => AiRiskLevel.Medium,
            < 0.7 => AiRiskLevel.High,
            _ => AiRiskLevel.Critical
        };

        Log.Debug("[AiDetection] Overall score: {Score}, Risk: {Risk}", overallScore, risk);

        return new DetectionReport
        {
            OverallRisk = risk,
            StatisticalScore = overallScore,
            Details = details
        };
    }

    // === 辅助方法 ===

    private static List<string> SplitSentences(string text)
    {
        var result = new List<string>();
        var current = new System.Text.StringBuilder();
        foreach (var c in text)
        {
            current.Append(c);
            if (c is '。' or '！' or '？' or '.' or '!' or '?' or '\n')
            {
                if (current.Length > 2)
                {
                    result.Add(current.ToString().Trim());
                    current.Clear();
                }
            }
        }
        if (current.Length > 2) result.Add(current.ToString().Trim());
        return result;
    }

    /// <summary>
    /// 简单情绪分数估算。基于情绪词出现频率。
    /// </summary>
    private static double EstimateEmotionScore(string sentence)
    {
        var positiveWords = new[] { "笑", "喜", "乐", "欢", "爱", "温暖", "感动", "幸福", "美好", "赞赏" };
        var negativeWords = new[] { "怒", "悲", "恨", "恐惧", "痛苦", "绝望", "哀", "愁", "焦虑", "愤怒" };

        var posCount = positiveWords.Count(w => sentence.Contains(w));
        var negCount = negativeWords.Count(w => sentence.Contains(w));
        var total = posCount + negCount;

        return total > 0 ? (double)posCount / total : 0.5;
    }
}
