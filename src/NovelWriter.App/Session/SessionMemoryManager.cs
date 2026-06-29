using System.IO;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace NovelWriter.App.Session;

/// <summary>
/// 单条会话事件。一次用户操作、AI 回复、阶段动作都对应一条。
/// 类型化而非纯文本，方便后续做主题检索和按阶段过滤。
/// </summary>
public class SessionEvent
{
    /// <summary>事件类型：UserMessage / AIMessage / SystemMessage / ErrorMessage / StageEvent / Directive</summary>
    public string Type { get; set; } = "";
    /// <summary>事件主体文本（用户/AI/系统消息）</summary>
    public string Text { get; set; } = "";
    /// <summary>可选的子类型（StageEvent 用：Start/Confirm/Rewrite/Fail/Compress/Clear）</summary>
    public string? SubType { get; set; }
    /// <summary>可选阶段标签：梗概/大纲/写作/聊天</summary>
    public string? Stage { get; set; }
    /// <summary>事件时间（UTC ISO 8601）</summary>
    public DateTime Timestamp { get; set; } = DateTime.UtcNow;
    /// <summary>近似 token 数（按字符/2 估算，中文保守）</summary>
    public int ApproxTokens { get; set; }
}

/// <summary>
/// 短期记忆：分层事件流。最近 10 条原文 + 早期事件 LLM 压缩摘要。
/// 序列化到 <c>session/short_term.json</c>。
/// </summary>
public class ShortTermMemory
{
    /// <summary>近期事件（原文，最多 10 条）</summary>
    public List<SessionEvent> Recent { get; set; } = new();
    /// <summary>早期事件的 LLM 压缩摘要（"近期 10 条之前的全部事件被压缩为以下要点..."）</summary>
    public string Summary { get; set; } = "";
    /// <summary>被 Summary 覆盖的原始事件数</summary>
    public int SummarySourceCount { get; set; }
    /// <summary>当前估算总 token 数（recent + summary 一起）</summary>
    public int EstimatedTokens { get; set; }
    /// <summary>最后更新时间</summary>
    public DateTime LastUpdated { get; set; } = DateTime.UtcNow;
}

/// <summary>
/// 长期记忆：累积的压缩摘要数组。append-only（用户主动压缩或清空时追加）。
/// 序列化到 <c>session/long_term.json</c>。
/// </summary>
public class LongTermMemory
{
    public List<LongTermEntry> Entries { get; set; } = new();
}

public class LongTermEntry
{
    /// <summary>唯一 ID（GUID，去连字符）</summary>
    public string Id { get; set; } = "";
    /// <summary>压缩/清空时间（UTC）</summary>
    public DateTime Timestamp { get; set; } = DateTime.UtcNow;
    /// <summary>"Compress" / "Clear" / "AutoCompress"</summary>
    public string Trigger { get; set; } = "";
    /// <summary>压缩后的结构化摘要（来自 LLM）</summary>
    public string Summary { get; set; } = "";
    /// <summary>本次压缩覆盖的源事件数</summary>
    public int SourceEventCount { get; set; }
    /// <summary>LLM 提取的关键词（用于主题检索）</summary>
    public List<string> KeyTopics { get; set; } = new();
}

/// <summary>
/// 会话记忆管理：短期/长期读写、token 估算、自动压缩触发。
/// 不依赖 DI/Engine 任何类 —— 纯文件 IO + JSON，可在 ShellWindow 直接 new。
/// </summary>
public class SessionMemoryManager
{
    private readonly string _sessionDir;
    private readonly string _shortPath;
    private readonly string _longPath;
    private static readonly JsonSerializerOptions JsonOpts = new()
    {
        WriteIndented = true,
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull
    };

    /// <summary>近期事件保留条数（用户选【分层事件】方案 = 10 条）</summary>
    public const int RecentKeepCount = 10;
    /// <summary>短期记忆 token 软上限 —— 超过触发自动压缩</summary>
    public const int TokenSoftLimit = 8000;
    /// <summary>单事件文本估算：中文/英文混排，按 2 字符/token 估算（比 1 字符保守）</summary>
    private static int EstimateTokens(string text) =>
        string.IsNullOrEmpty(text) ? 0 : Math.Max(1, text.Length / 2);

    public ShortTermMemory Short { get; private set; } = new();
    public LongTermMemory Long { get; private set; } = new();

    public SessionMemoryManager(string projectDir)
    {
        _sessionDir = Path.Combine(projectDir, "session");
        Directory.CreateDirectory(_sessionDir);
        _shortPath = Path.Combine(_sessionDir, "short_term.json");
        _longPath = Path.Combine(_sessionDir, "long_term.json");
        Load();
    }

    // === 持久化 ===

    public void Load()
    {
        try
        {
            if (File.Exists(_shortPath))
                Short = JsonSerializer.Deserialize<ShortTermMemory>(File.ReadAllText(_shortPath)) ?? new();
        }
        catch { Short = new(); }
        try
        {
            if (File.Exists(_longPath))
                Long = JsonSerializer.Deserialize<LongTermMemory>(File.ReadAllText(_longPath)) ?? new();
        }
        catch { Long = new(); }
    }

    public void SaveShort() => SafeWrite(_shortPath, Short);
    public void SaveLong() => SafeWrite(_longPath, Long);

    private static void SafeWrite(string path, object obj)
    {
        try
        {
            var tmp = path + ".tmp";
            File.WriteAllText(tmp, JsonSerializer.Serialize(obj, JsonOpts));
            File.Move(tmp, path, overwrite: true);
        }
        catch { /* 写入失败不影响主流程 */ }
    }

    // === 事件追加 ===

    /// <summary>
    /// 追加一条事件。会自动更新 token 估算；超过软上限时返回 true 让调用者决定是否压缩。
    /// </summary>
    public bool AppendEvent(SessionEvent ev)
    {
        ev.ApproxTokens = EstimateTokens(ev.Text);
        Short.Recent.Add(ev);
        RecalculateTokens();
        Short.LastUpdated = DateTime.UtcNow;
        SaveShort();
        return Short.EstimatedTokens > TokenSoftLimit;
    }

    private void RecalculateTokens()
    {
        var recentTokens = Short.Recent.Sum(e => e.ApproxTokens);
        var summaryTokens = EstimateTokens(Short.Summary);
        Short.EstimatedTokens = recentTokens + summaryTokens;
    }

    // === 压缩：把超出 RecentKeepCount 的事件合并到 Summary ===

    /// <summary>
    /// 取出待压缩的事件（recent 中前 N - RecentKeepCount 条），返回它们让调用者用 LLM 生成 summary。
    /// 调用方拿到 summary 后调 <see cref="FinalizeCompression"/> 写回。
    /// </summary>
    public List<SessionEvent> TakeEventsToCompress()
    {
        if (Short.Recent.Count <= RecentKeepCount) return new();
        var count = Short.Recent.Count - RecentKeepCount;
        var toCompress = Short.Recent.Take(count).ToList();
        return toCompress;
    }

    /// <summary>
    /// LLM 生成 summary 后的回调。把 events 真正"下沉"到 summary，并保留 recent 里 RecentKeepCount 条。
    /// </summary>
    public void FinalizeCompression(string newSummary, List<SessionEvent> compressedEvents, string trigger = "Compress")
    {
        // 1. 现有 summary 拼上新 summary
        var combined = string.IsNullOrWhiteSpace(Short.Summary)
            ? newSummary
            : $"{Short.Summary}\n\n--- {DateTime.Now:yyyy-MM-dd HH:mm} ---\n{newSummary}";
        Short.Summary = combined;
        Short.SummarySourceCount += compressedEvents.Count;
        // 2. 删掉已压缩的事件（保留最后 RecentKeepCount 条）
        Short.Recent = Short.Recent.Skip(compressedEvents.Count).ToList();
        RecalculateTokens();
        Short.LastUpdated = DateTime.UtcNow;
        SaveShort();
    }

    /// <summary>
    /// 把当前短期记忆全量追加到长期记忆，然后清空短期。
    /// 触发条件：用户点"清空"按钮，或短期超限自动清空。
    /// </summary>
    public void ArchiveToLongTerm(string summary, string trigger = "Clear")
    {
        var sourceCount = Short.Recent.Count + (string.IsNullOrEmpty(Short.Summary) ? 0 : 1);
        // 简单的关键词提取：取所有 event text 的高频 2-gram（中文按 1-gram）。这里保守取前 100 字符的实体词。
        var keyTopics = ExtractKeyTopics(summary);
        Long.Entries.Add(new LongTermEntry
        {
            Id = Guid.NewGuid().ToString("N"),
            Timestamp = DateTime.UtcNow,
            Trigger = trigger,
            Summary = summary,
            SourceEventCount = sourceCount,
            KeyTopics = keyTopics
        });
        SaveLong();
    }

    /// <summary>
    /// 清空短期记忆（不写长期）。用户点"清空上下文"时使用 —— 如果没压缩就清空，相当于丢弃。
    /// </summary>
    public void ClearShortTermWithoutArchive()
    {
        Short = new ShortTermMemory();
        SaveShort();
    }

    // === 注入到 system prompt ===

    /// <summary>
    /// 把短期/长期记忆拼成可注入 system prompt 的 Markdown 块。
    /// 短期按 token 截断（防止 prompt 本身超限），长期保留全部。
    /// </summary>
    public string ToPromptSection(int maxSectionTokens = 1500)
    {
        var sb = new StringBuilder();
        sb.AppendLine("\n【会话记忆】");

        // 长期记忆：先列（更早的信息）
        if (Long.Entries.Count > 0)
        {
            sb.AppendLine($"长期记忆（{Long.Entries.Count} 条压缩摘要）:");
            foreach (var e in Long.Entries.TakeLast(5)) // 只列最近 5 条
            {
                sb.AppendLine($"- [{e.Timestamp:yyyy-MM-dd HH:mm} | {e.Trigger} | {e.SourceEventCount} 事件]");
                var trimmed = e.Summary.Length > 300 ? e.Summary[..300] + "…" : e.Summary;
                sb.AppendLine($"  {trimmed}");
            }
        }

        // 短期记忆：分层显示
        if (!string.IsNullOrEmpty(Short.Summary))
        {
            sb.AppendLine($"\n短期记忆 - 早期事件摘要（{Short.SummarySourceCount} 条事件）:");
            var summary = Short.Summary.Length > 800 ? Short.Summary[..800] + "…" : Short.Summary;
            sb.AppendLine(summary);
        }

        if (Short.Recent.Count > 0)
        {
            sb.AppendLine($"\n短期记忆 - 近期 {Short.Recent.Count} 条事件（按时间正序）:");
            foreach (var e in Short.Recent)
            {
                var prefix = e.Type switch
                {
                    "UserMessage" => "用户",
                    "AIMessage" => "AI",
                    "SystemMessage" => "系统",
                    "ErrorMessage" => "错误",
                    "StageEvent" => $"阶段[{e.SubType}]",
                    "Directive" => "改写指令",
                    _ => e.Type
                };
                var t = e.Text.Length > 200 ? e.Text[..200] + "…" : e.Text;
                sb.AppendLine($"  [{e.Timestamp:HH:mm:ss}] {prefix}: {t}");
            }
        }

        var result = sb.ToString();
        // 整段截断防 prompt 爆炸
        if (EstimateTokens(result) > maxSectionTokens)
        {
            var maxChars = maxSectionTokens * 2;
            if (result.Length > maxChars)
                result = result[..maxChars] + "\n…(会话记忆过长已截断)";
        }
        return result;
    }

    public int TotalEvents =>
        Short.Recent.Count + Short.SummarySourceCount + Long.Entries.Sum(e => e.SourceEventCount);

    // === 简单关键词提取（无 LLM 依赖） ===

    private static List<string> ExtractKeyTopics(string text)
    {
        if (string.IsNullOrWhiteSpace(text)) return new();
        // 提取 # 标题、引号、书名号里的内容作为候选 topic
        var rx = new System.Text.RegularExpressions.Regex("[#\"\"《》（）()]", System.Text.RegularExpressions.RegexOptions.None);
        var candidates = rx.Split(text)
            .Where(s => s.Length >= 2 && s.Length <= 20)
            .Take(8)
            .ToList();
        return candidates;
    }
}
