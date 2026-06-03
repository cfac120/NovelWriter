using NovelWriter.Core.Entities;
using NovelWriter.Core.Interfaces;
using Serilog;

namespace NovelWriter.Engine.Style;

/// <summary>
/// 插曲导入器。从本地目录扫描 .txt 文章，
/// 从文本中提取适合作为插曲的典故/轶事，写入插曲库。
/// </summary>
public class InterludeImporter
{
    private readonly ILlmAdapter _llm;
    private readonly IInterludeRepository _repo;

    public InterludeImporter(ILlmAdapter llm, IInterludeRepository repo)
    {
        _llm = llm;
        _repo = repo;
    }

    /// <summary>
    /// 从指定目录导入所有 .txt 文件的插曲条目。
    /// 每篇文章通过 LLM 提取 1-3 个可改编的历史/轶事片段。
    /// </summary>
    public async Task<InterludeImportResult> ImportFromDirectoryAsync(
        string directoryPath, CancellationToken ct = default)
    {
        if (!Directory.Exists(directoryPath))
            return new InterludeImportResult { Error = $"目录不存在: {directoryPath}" };

        var files = Directory.GetFiles(directoryPath, "*.txt", SearchOption.TopDirectoryOnly);
        if (files.Length == 0)
            return new InterludeImportResult { Error = $"目录中无 .txt 文件: {directoryPath}" };

        var result = new InterludeImportResult();
        var existing = await _repo.GetAvailableInterludesAsync();
        var existingFacts = new HashSet<string>(existing.Select(e => e.CoreFact));

        foreach (var file in files)
        {
            ct.ThrowIfCancellationRequested();

            try
            {
                var text = await File.ReadAllTextAsync(file, ct);
                if (text.Length < 100) continue;

                // 取前3000字供LLM分析
                var sample = text[..Math.Min(text.Length, 3000)];
                var entries = await ExtractInterludesAsync(sample, Path.GetFileName(file), ct);

                foreach (var item in entries)
                {
                    if (existingFacts.Contains(item.CoreFact))
                    {
                        result.Skipped.Add(item.CoreFact.Truncate(30));
                        continue;
                    }

                    var entry = new InterludeEntry
                    {
                        Id = $"EP_{Guid.NewGuid():N}"[..12],
                        SourceType = item.SourceType,
                        Source = item.Source,
                        CoreFact = item.CoreFact,
                        NarrativeHook = item.NarrativeHook,
                        AdaptableThemes = item.AdaptableThemes,
                        SuggestedGenres = item.SuggestedGenres,
                        CreatedAt = DateTime.UtcNow
                    };
                    result.Imported.Add(entry);
                    existingFacts.Add(entry.CoreFact);
                }
            }
            catch (Exception ex)
            {
                result.Failed.Add((Path.GetFileName(file), ex.Message));
                Log.Error(ex, "[InterludeImport] Failed to process '{File}'", file);
            }
        }

        return result;
    }

    private async Task<List<InterludeEntry>> ExtractInterludesAsync(
        string text, string fileName, CancellationToken ct)
    {
        var prompt = """
            你是历史典故提取助手。从文本中提取可作为"插曲闲笔"的典故片段。
            每条输出一个JSON对象，格式:
            {
              "core_fact": "核心事实（≤50字）",
              "narrative_hook": "叙事钩子（一句话）",
              "adaptable_themes": ["主题1","主题2"],
              "suggested_genres": ["题材1","题材2"]
            }
            只输出有效的JSON数组，最多3条。
            """;

        var response = await _llm.ChatAsync(prompt, $"文本: {text}", ct);

        try
        {
            var json = ExtractJson(response);
            var dtos = System.Text.Json.JsonSerializer.Deserialize<List<InterludeDto>>(json);
            if (dtos == null) return [];

            return dtos.Select(d => new InterludeEntry
            {
                SourceType = "file_import",
                Source = fileName,
                CoreFact = d.core_fact ?? "",
                NarrativeHook = d.narrative_hook ?? "",
                AdaptableThemes = System.Text.Json.JsonSerializer.Serialize(d.adaptable_themes ?? []),
                SuggestedGenres = System.Text.Json.JsonSerializer.Serialize(d.suggested_genres ?? []),
                CreatedAt = DateTime.UtcNow
            }).ToList();
        }
        catch (Exception ex)
        {
            Log.Warning(ex, "[InterludeImport] Failed to parse LLM response for '{File}'", fileName);
            return [];
        }
    }

    private static string ExtractJson(string response)
    {
        var start = response.IndexOf('[');
        var end = response.LastIndexOf(']');
        if (start >= 0 && end > start)
            return response[start..(end + 1)];
        start = response.IndexOf('{');
        end = response.LastIndexOf('}');
        if (start >= 0 && end > start)
            return "[" + response[start..(end + 1)] + "]";
        return "[]";
    }

    private record InterludeDto(string? core_fact, string? narrative_hook,
        List<string>? adaptable_themes, List<string>? suggested_genres);
}

public class InterludeImportResult
{
    public List<InterludeEntry> Imported { get; init; } = [];
    public List<string> Skipped { get; init; } = [];
    public List<(string File, string Reason)> Failed { get; init; } = [];
    public string? Error { get; set; }
    public int Total => Imported.Count + Skipped.Count + Failed.Count;
}
