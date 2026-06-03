using NovelWriter.Core.Entities;
using NovelWriter.Core.Interfaces;
using Serilog;

namespace NovelWriter.Engine.Style;

/// <summary>
/// 风格导入器。从本地目录扫描 .txt 文章，
/// 调用 StyleExtractionAgent 提取风格档案，写入全局风格库。
/// </summary>
public class StyleImporter
{
    private readonly StyleExtractionAgent _agent;
    private readonly IStyleLibraryRepository _repo;

    public StyleImporter(StyleExtractionAgent agent, IStyleLibraryRepository repo)
    {
        _agent = agent;
        _repo = repo;
    }

    /// <summary>
    /// 从指定目录导入所有 .txt 文件的风格档案。
    /// 文件名作为来源标识（如 "斗破苍穹_天蚕土豆.txt"）。
    /// </summary>
    public async Task<StyleImportResult> ImportFromDirectoryAsync(
        string directoryPath, CancellationToken ct = default)
    {
        if (!Directory.Exists(directoryPath))
            return new StyleImportResult { Error = $"目录不存在: {directoryPath}" };

        var files = Directory.GetFiles(directoryPath, "*.txt", SearchOption.TopDirectoryOnly);
        if (files.Length == 0)
            return new StyleImportResult { Error = $"目录中无 .txt 文件: {directoryPath}" };

        var result = new StyleImportResult();
        var existing = await _repo.GetAvailableStylesAsync();
        var existingTitles = new HashSet<string>(existing.Select(s => s.SourceTitle));

        foreach (var file in files)
        {
            ct.ThrowIfCancellationRequested();

            var (title, author) = ParseFileName(Path.GetFileNameWithoutExtension(file));
            if (existingTitles.Contains(title))
            {
                result.Skipped.Add(title);
                Log.Information("[StyleImport] Skipped '{Title}' — already exists", title);
                continue;
            }

            try
            {
                var text = await File.ReadAllTextAsync(file, ct);
                if (text.Length < 200)
                {
                    result.Failed.Add((title, "文章太短(<200字)"));
                    continue;
                }

                // 取前5000字用于风格提取（节省 LLM 成本）
                var sampleText = text[..Math.Min(text.Length, 5000)];
                var profile = await _agent.ExtractAsync(sampleText, title, author, ct);

                if (profile != null)
                {
                    result.Imported.Add(profile);
                    Log.Information("[StyleImport] Imported '{Title}' by {Author}", title, author);
                }
                else
                {
                    result.Failed.Add((title, "LLM提取失败"));
                }
            }
            catch (Exception ex)
            {
                result.Failed.Add((title, ex.Message));
                Log.Error(ex, "[StyleImport] Failed to import '{Title}'", title);
            }
        }

        return result;
    }

    /// <summary>
    /// 解析文件名: "斗破苍穹_天蚕土豆.txt" → (斗破苍穹, 天蚕土豆)
    /// "斗破苍穹.txt" → (斗破苍穹, 未知作者)
    /// </summary>
    private static (string title, string author) ParseFileName(string name)
    {
        var idx = name.LastIndexOf('_');
        if (idx > 0 && idx < name.Length - 1)
            return (name[..idx], name[(idx + 1)..]);
        return (name, "未知作者");
    }
}

public class StyleImportResult
{
    public List<StyleProfile> Imported { get; init; } = [];
    public List<string> Skipped { get; init; } = [];
    public List<(string Title, string Reason)> Failed { get; init; } = [];
    public string? Error { get; set; }
    public int Total => Imported.Count + Skipped.Count + Failed.Count;
}
