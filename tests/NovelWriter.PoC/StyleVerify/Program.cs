using Microsoft.EntityFrameworkCore;
using NovelWriter.Core.Entities;
using NovelWriter.Core.ValueObjects;
using NovelWriter.Engine.Llm;
using NovelWriter.Engine.Style;
using NovelWriter.Storage;
using NovelWriter.Storage.Repositories;

// 风格库/插曲验证
var apiKey = Environment.GetEnvironmentVariable("DEEPSEEK_API_KEY")
    ?? throw new InvalidOperationException("请设置 DEEPSEEK_API_KEY");

using var http = new HttpClient { Timeout = TimeSpan.FromMinutes(5) };
var adapter = new DeepSeekAdapter(http, apiKey);

var db = new NovelWriterDbContext(new DbContextOptionsBuilder<NovelWriterDbContext>()
    .UseSqlite("Data Source=style_test.db").Options);
db.Database.EnsureCreated();

var projectRepo = new ProjectRepository(db);
var styleRepo = new StyleLibraryRepository(db);
var interludeRepo = new InterludeRepository(db);
var styleInjector = new StyleInjector(styleRepo);

Console.WriteLine("=== 风格库 & 插曲系统验证 ===\n");

// 创建测试项目
var project = new Project { Title = "验证", Genre = "仙侠", Status = NovelWriter.Core.Enums.ProjectStatus.Active };
await projectRepo.AddAsync(project);
var pid = project.Id;

// 种子数据
if (!db.StyleProfiles.Any())
{
    db.StyleProfiles.AddRange(
        new StyleProfile { Id = "STYLE_001", SourceTitle = "聊斋志异", SourceAuthor = "蒲松龄", ProfileJson = "句式: 文白夹杂，典雅精炼\n叙事: 简洁直叙，善用留白", Tags = "[\"志怪\",\"文言\"]" },
        new StyleProfile { Id = "STYLE_002", SourceTitle = "水浒传", SourceAuthor = "施耐庵", ProfileJson = "句式: 长短交错，对话传神\n叙事: 线性推进，情节爽快", Tags = "[\"侠义\",\"爽文\"]" },
        new StyleProfile { Id = "STYLE_003", SourceTitle = "世说新语", SourceAuthor = "刘义庆", ProfileJson = "句式: 极简精悍，点到为止\n叙事: 片段式，回味悠长", Tags = "[\"古典\",\"含蓄\"]" }
    );
    db.InterludeEntries.AddRange(
        new InterludeEntry { Id = "EP_001", SourceType = "historical", Source = "史记", CoreFact = "荆轲刺秦，易水送别: 风萧萧兮易水寒，壮士一去兮不复还。", NarrativeHook = "绝境中的壮烈抉择", AdaptableThemes = "[\"赴死\",\"忠义\"]" },
        new InterludeEntry { Id = "EP_002", SourceType = "anecdote", Source = "庄子", CoreFact = "庖丁解牛，以无厚入有间，游刃有余。", NarrativeHook = "技艺臻于化境", AdaptableThemes = "[\"修炼\",\"顿悟\"]" },
        new InterludeEntry { Id = "EP_003", SourceType = "historical", Source = "出师表", CoreFact = "臣本布衣，躬耕于南阳，三顾臣于草庐之中。", NarrativeHook = "知遇之恩与使命担当", AdaptableThemes = "[\"恩义\",\"责任\"]" }
    );
    await db.SaveChangesAsync();
    Console.WriteLine("[种子] 3风格 + 3插曲 已入库\n");
}

// 验证1: 风格随机选择
Console.WriteLine("--- 验证1: 风格随机选择(3章,卷内不重复) ---");
var used = new HashSet<string>();
for (int ch = 1; ch <= 3; ch++)
{
    var (p, _) = await styleInjector.SelectRandomStyleAsync(pid, 1, ch);
    bool dup = p != null && !used.Add(p.Id);
    Console.WriteLine($"  第{ch}章 → {p?.Id ?? "无"} ({p?.SourceTitle ?? "-"}) {(dup ? "重复!" : "")}");
}
Console.WriteLine($"  去重: {(used.Count == 3 ? "PASS" : "FAIL")}\n");

// 验证2: 插曲改编
Console.WriteLine("--- 验证2: 插曲LLM改编 ---");
var inter = await interludeRepo.GetRandomUnusedInVolumeAsync(pid, 1);
if (inter != null)
{
    Console.WriteLine($"  原典: {inter.CoreFact.Truncate(50)}");

    var adapted = await adapter.ChatAsync(
        "你是改编专家，将典故转为修仙闲笔(≤100字)。",
        $"典故: {inter.CoreFact}\n钩子: {inter.NarrativeHook}\n直接输出改编:",
        CancellationToken.None);

    Console.WriteLine($"  改编: {adapted.Truncate(150)}");
    Console.WriteLine($"  字数: {adapted.Length} {(adapted.Length <= 100 ? "OK" : "超标")}");
    await interludeRepo.LogUsageAsync(inter.Id, pid, 1, 1);
}

// 验证3: 风格注入写作
Console.WriteLine("\n--- 验证3: 风格注入写作 ---");
var (style, block) = await styleInjector.SelectRandomStyleAsync(pid, 1, 10);
if (style != null)
{
    Console.Write($"  [{style.SourceTitle}] 生成中 ");
    var sb = new System.Text.StringBuilder();
    await foreach (var c in adapter.StreamChatAsync(
        $"你是网文作家。{block}\n写300-500字场景。",
        "场景: 深夜山顶，明月当空，主角盘膝领悟剑意。",
        CancellationToken.None))
    {
        sb.Append(c);
        if (sb.Length % 100 == 0) Console.Write(".");
    }
    Console.WriteLine($"\n  结果: {sb.Length}字, 预览: {sb.ToString().Truncate(200)}");
}

Console.WriteLine("\n=== 验证完成 ===");
db.Dispose();

static class Ext { public static string Truncate(this string v, int m) => string.IsNullOrEmpty(v) || v.Length <= m ? v : v[..m] + "..."; }
