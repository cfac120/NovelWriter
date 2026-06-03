using System.Text.RegularExpressions;

namespace NovelWriter.Core.DomainServices;

public static class KeywordExtractor
{
    private static readonly HashSet<string> StopWords = [
        "的", "了", "在", "是", "我", "有", "和", "就", "不", "人", "都", "一", "一个",
        "上", "也", "很", "到", "说", "要", "去", "你", "会", "着", "没有", "看", "好",
        "自己", "这", "他", "她", "它"
    ];

    public static List<string> Extract(string text, int maxKeywords = 10)
    {
        var words = Regex.Matches(text, @"[\u4e00-\u9fff]{2,4}")
            .Select(m => m.Value)
            .Where(w => !StopWords.Contains(w))
            .GroupBy(w => w)
            .OrderByDescending(g => g.Count())
            .Take(maxKeywords)
            .Select(g => g.Key)
            .ToList();
        return words;
    }
}
