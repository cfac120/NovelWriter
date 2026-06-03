using Microsoft.EntityFrameworkCore;
using NovelWriter.Core.Entities;
using NovelWriter.Core.Enums;
using NovelWriter.Core.Memory;
using NovelWriter.Core.ValueObjects;
using NovelWriter.Engine.ContextWindow;
using NovelWriter.Engine.Llm;
using NovelWriter.Engine.Memory;
using NovelWriter.Engine.SelfCheck;
using NovelWriter.Storage;
using NovelWriter.Storage.Repositories;

// ============================================================
// NovelWriter PoC — 记忆一致性验证（逐章模式）
// 用法: dotnet run -- 5        → 只生成第5章
//       dotnet run -- 1 10     → 生成第1-10章
//       dotnet run             → 生成全部30章
// ============================================================

var argsList = args.ToList();
int startChapter = 1, endChapter = 30;

if (argsList.Count >= 1 && int.TryParse(argsList[0], out var s))
{
    startChapter = s;
    endChapter = argsList.Count >= 2 && int.TryParse(argsList[1], out var e) ? e : s;
}

var apiKey = Environment.GetEnvironmentVariable("DEEPSEEK_API_KEY")
    ?? throw new InvalidOperationException("请设置 DEEPSEEK_API_KEY 环境变量");

Console.WriteLine($"=== NovelWriter PoC: 第{startChapter}-{endChapter}章 ===");
Console.WriteLine($"开始时间: {DateTime.Now:yyyy-MM-dd HH:mm:ss}");
Console.WriteLine();

using var httpClient = new HttpClient { Timeout = TimeSpan.FromMinutes(10) };
var adapter = new DeepSeekAdapter(httpClient, apiKey);
var dbOptions = new DbContextOptionsBuilder<NovelWriterDbContext>()
    .UseSqlite("Data Source=poc_shared.db")
    .Options;
using var db = new NovelWriterDbContext(dbOptions);
db.Database.EnsureCreated();

var projectRepo = new ProjectRepository(db);
var chapterRepo = new ChapterRepository(db);
var outlineRepo = new OutlineRepository(db);
var memoryRepo = new MemoryRepository(db);
var tokenCounter = new TokenCounter();

var contextCompiler = new ContextWindowCompiler(memoryRepo, chapterRepo, outlineRepo, tokenCounter);
var memoryAgent = new MemoryManagerAgent(adapter, memoryRepo, chapterRepo, tokenCounter, "deepseek-v4-pro");
var validator = new MemoryChangeValidator(memoryRepo);
var selfCheck = new SelfCheckRunner(adapter, memoryRepo, chapterRepo, outlineRepo);

// 初始化场景（幂等：已存在则跳过）
Console.WriteLine(">>> 初始化测试场景");
var projects = await projectRepo.GetAllAsync();
var project = projects.FirstOrDefault(p => p.Title == "PoC测试-剑道独尊");
ProjectId projectId;
bool isNewDb = project == null;

if (isNewDb)
{
    project = new Project { Title = "PoC测试-剑道独尊", Genre = "仙侠", Status = ProjectStatus.Active };
    await projectRepo.AddAsync(project);
    projectId = project.Id;

    var charLinFeng = new CharacterProfile { CharacterId = new CharacterId(1), ProjectId = projectId, Name = "林风", Profile = "核心特质: 坚毅, 正直, 重视情义\n禁止特质: 懦弱, 贪婪, 背叛朋友\n背景: 山村少年, 意外获得古剑传承", Version = 1 };
    var charSuYun = new CharacterProfile { CharacterId = new CharacterId(2), ProjectId = projectId, Name = "苏云", Profile = "核心特质: 聪慧, 冷静, 医术高超\n禁止特质: 鲁莽, 见死不救\n背景: 医药世家传人, 因家族被害踏上复仇之路", Version = 1 };
    var charMoTian = new CharacterProfile { CharacterId = new CharacterId(3), ProjectId = projectId, Name = "魔天", Profile = "核心特质: 野心勃勃, 智谋深远, 不择手段\n背景: 魔教教主, 实则心系天下苍生", Version = 1 };
    await memoryRepo.AddCharacterProfileVersionAsync(charLinFeng);
    await memoryRepo.AddCharacterProfileVersionAsync(charSuYun);
    await memoryRepo.AddCharacterProfileVersionAsync(charMoTian);

    for (int i = 1; i <= 5; i++)
        await memoryRepo.AddWorldSettingAsync(new WorldSetting { WorldSettingId = new WorldSettingId(i), ProjectId = projectId, Name = i switch { 1 => "修行境界体系", 2 => "古剑传承", 3 => "五大学院", 4 => "天魔大战", _ => "灵脉分布" }, Description = $"世界观设定 {i}", Version = 1 });

    await memoryRepo.AddForeshadowingAsync(new Foreshadowing { ForeshadowingId = new ForeshadowingId(1), ProjectId = projectId, VolumeNumber = 1, Description = "黑色玉佩中的神秘力量", Status = ForeshadowingStatus.Active, Priority = ForeshadowingPriority.High, PlantedBy = PlantedBy.Manual, PlantedChapter = 1 });
    await memoryRepo.AddForeshadowingAsync(new Foreshadowing { ForeshadowingId = new ForeshadowingId(2), ProjectId = projectId, VolumeNumber = 1, Description = "林风与苏云的命运羁绊", Status = ForeshadowingStatus.Active, Priority = ForeshadowingPriority.High, PlantedBy = PlantedBy.Manual, PlantedChapter = 3 });
    await memoryRepo.AddForeshadowingAsync(new Foreshadowing { ForeshadowingId = new ForeshadowingId(3), ProjectId = projectId, VolumeNumber = 1, Description = "魔天的真实身份", Status = ForeshadowingStatus.Active, Priority = ForeshadowingPriority.High, PlantedBy = PlantedBy.Manual, PlantedChapter = 5 });
    Console.WriteLine("  新项目已创建");
}
else
{
    projectId = project.Id;
    Console.WriteLine("  复用已有项目");
}

// 大纲（仅补全新章节）
for (int ch = 1; ch <= endChapter; ch++)
{
    var existing = await outlineRepo.GetByChapterNumberAsync(projectId, 1, ch);
    if (existing == null)
    {
        await outlineRepo.AddAsync(new Outline
        {
            ProjectId = projectId, VolumeNumber = 1, ChapterNumber = ch,
            KeyEvents = ch switch { 1 => "林风获得古剑传承, 发现黑色玉佩", 3 => "林风与苏云初次相遇于古剑遗迹", 5 => "魔天首次登场", 10 => "学院大比", 15 => "魔天揭露部分真相", 20 => "三主角集结", 25 => "黑衣人暴露", 30 => "最终决战", _ => $"第{ch}章关键事件" },
            CharacterInvolvement = ch <= 2 ? "CHAR_001" : ch <= 4 ? "CHAR_001,CHAR_002" : "CHAR_001,CHAR_002,CHAR_003",
            SceneDescription = ch switch { 1 => "山村清晨, 少年林风在后山发现古剑遗迹", 3 => "古剑遗迹深处, 林风与苏云争夺古剑", 5 => "魔教大殿, 魔天召见林风", 10 => "五大学院联合大比", 15 => "月下密谈", 20 => "万里长城要塞集结", 25 => "密室对峙, 黑衣人露真面目", 30 => "九天之上最终决战", _ => $"第{ch}章场景" }
        });
    }
}

Console.WriteLine($"  角色: 3 | 世界观: 5 | 伏笔: 3 | 大纲: {endChapter}章");
Console.WriteLine();

// 逐章生成
Console.WriteLine(">>> 开始逐章生成");
Console.WriteLine();

for (int chapter = startChapter; chapter <= endChapter; chapter++)
{
    Console.WriteLine($"--- 第{chapter}章 ---");
    try
    {
        var outline = await outlineRepo.GetByChapterNumberAsync(projectId, 1, chapter)
            ?? throw new InvalidOperationException($"Outline not found for chapter {chapter}");

        // Stage04: 编译 ContextWindow
        Console.Write("  [编译] ");
        var compiled = await contextCompiler.CompileAsync(projectId, 1, chapter, "deepseek-v4-pro", CancellationToken.None);
        Console.WriteLine($"System={compiled.TokenBudget.SystemPrompt}+L1={compiled.TokenBudget.L1Context}+L2={compiled.TokenBudget.L2Context}+L3={compiled.TokenBudget.L3Context} tokens");

        // Stage05: 流式生成
        Console.Write("  [生成] ");
        var chapterContent = new System.Text.StringBuilder();
        const int maxChars = 4500;
        await foreach (var chunk in adapter.StreamChatAsync(compiled.SystemPrompt, compiled.UserMessage, CancellationToken.None))
        {
            if (chapterContent.Length + chunk.Length > maxChars)
            {
                var remaining = maxChars - chapterContent.Length;
                if (remaining > 0) chapterContent.Append(chunk[..remaining]);
                break;
            }
            chapterContent.Append(chunk);
            if (chapterContent.Length % 200 == 0) Console.Write(".");
        }
        var trimmed = chapterContent.Length >= maxChars ? " [截断]" : "";
        Console.WriteLine($" {chapterContent.Length}字{trimmed}");

        var chap = new Chapter
        {
            ProjectId = projectId, VolumeNumber = 1, ChapterNumber = chapter,
            Title = outline.SceneDescription?.Length > 30 ? outline.SceneDescription[..30] : $"第{chapter}章",
            Content = chapterContent.ToString(), WordCount = chapterContent.Length, Status = ChapterStatus.Draft
        };
        await chapterRepo.AddAsync(chap);

        // Stage06: 记忆提取
        Console.Write("  [记忆] Call1摘要 ");
        var summary = await memoryAgent.GenerateSummaryAsync(projectId, 1, chapter, chap, outline, CancellationToken.None);
        Console.Write("→ Call2结构检测 ");
        var extraction = await memoryAgent.ExtractStructuralChangesAsync(projectId, 1, chapter, chap, outline, summary, CancellationToken.None);
        extraction = await validator.ValidateAndFilterAsync(extraction, projectId, 1, CancellationToken.None);
        await memoryRepo.WriteMemoryChangesAsync(extraction, []);

        Console.WriteLine($"{extraction.ForeshadowingResolutions.Count}R/{extraction.NewForeshadowings.Count}F/{extraction.L3ChangeProposals.Count}L3");
        if (extraction.NeedsConfirmationItems.Count > 0)
            Console.WriteLine($"  [确认] {extraction.NeedsConfirmationItems.Count}条待确认: {string.Join(", ", extraction.NeedsConfirmationItems.Select(c => c.Summary))}");

        // SelfCheck 每5章
        if (chapter % 5 == 0)
        {
            Console.Write("  [SelfCheck] ");
            var report = await selfCheck.RunFullCheckAsync(projectId, 1, 1, chapter, [], CancellationToken.None);
            Console.WriteLine($"{report.Deviations.Count}条偏差, Critical={report.CriticalCount}, High={report.HighCount}");
            if (report.Deviations.Count > 0)
                foreach (var d in report.Deviations)
                    Console.WriteLine($"    [{d.Severity}] 第{d.DetectedChapter}章: {d.Description}");
        }

        Console.WriteLine("  ✓ 完成");
    }
    catch (Exception ex)
    {
        Console.ForegroundColor = ConsoleColor.Red;
        Console.WriteLine($"  ✗ 失败: {ex.Message}");
        Console.ResetColor();
    }
    Console.WriteLine();
}

// 报告
Console.WriteLine("========== 验证报告 ==========");
var activeFs = await memoryRepo.GetActiveForeshadowingsAsync(projectId, 1);
Console.WriteLine($"伏笔: 活跃{activeFs.Count(f => f.Status == ForeshadowingStatus.Active)}条, 已回收{activeFs.Count(f => f.Status == ForeshadowingStatus.Resolved)}条");
var chars = await memoryRepo.GetCharacterProfilesAsync(projectId);
Console.WriteLine($"人物档案: {chars.Count}个");
var summaries = await memoryRepo.GetRecentSummariesAsync(projectId, endChapter);
Console.WriteLine($"章节摘要: {summaries.Count}篇");
Console.WriteLine("========== 报告结束 ==========");

db.Database.CloseConnection();
return 0;
