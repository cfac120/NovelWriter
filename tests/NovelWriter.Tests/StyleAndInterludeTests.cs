using Microsoft.EntityFrameworkCore;
using NovelWriter.Core.Entities;
using NovelWriter.Core.Enums;
using NovelWriter.Core.Interfaces;
using NovelWriter.Core.ValueObjects;
using NovelWriter.Engine.Style;
using NovelWriter.Storage;
using NovelWriter.Storage.Repositories;

namespace NovelWriter.Tests;

// ============================================================
// 风格库: 全局共享 + 从本地目录导入 + 卷内去重
// ============================================================
public class StyleLibraryGlobalTests
{
    [Fact]
    public async Task StyleLibrary_SharedAcrossProjects_ReturnsSamePool()
    {
        await using var db = TestDbHelper.CreateInMemoryDb();
        var repo = new StyleLibraryRepository(db);

        // 直接向库中添加两条风格（模拟导入后）
        db.StyleProfiles.AddRange(
            new StyleProfile { Id = "S1", SourceTitle = "斗破苍穹", SourceAuthor = "天蚕土豆",
                ProfileJson = "爽文节奏", Tags = "[\"玄幻\"]" },
            new StyleProfile { Id = "S2", SourceTitle = "诡秘之主", SourceAuthor = "爱潜水的乌贼",
                ProfileJson = "悬疑克苏鲁", Tags = "[\"奇幻\"]" }
        );
        await db.SaveChangesAsync();

        // 全局风格库不区分项目
        var available = await repo.GetAvailableStylesAsync();
        Assert.Equal(2, available.Count);
        Assert.Contains(available, s => s.SourceTitle == "斗破苍穹");
    }

    [Fact]
    public async Task StyleLibrary_VolumeDedup_WorksCorrectly()
    {
        await using var db = TestDbHelper.CreateInMemoryDb();
        var repo = new StyleLibraryRepository(db);

        db.StyleProfiles.AddRange(
            new StyleProfile { Id = "A", SourceTitle = "遮天", SourceAuthor = "辰东",
                ProfileJson = "史诗", Tags = "[\"仙侠\"]" },
            new StyleProfile { Id = "B", SourceTitle = "间客", SourceAuthor = "猫腻",
                ProfileJson = "细腻", Tags = "[\"科幻\"]" }
        );
        await db.SaveChangesAsync();

        // 卷1用了A
        await repo.LogUsageAsync("A", 1, 1);
        await db.SaveChangesAsync();

        // 卷1只剩下B
        var r = await repo.GetRandomUnUsedInVolumeAsync(1);
        Assert.NotNull(r);
        Assert.Equal("B", r!.Id);
    }

    [Fact]
    public async Task StyleLibrary_VolumeExhausted_ReturnsNull()
    {
        await using var db = TestDbHelper.CreateInMemoryDb();
        var repo = new StyleLibraryRepository(db);
        var injector = new StyleInjector(repo);

        db.StyleProfiles.Add(
            new StyleProfile { Id = "X", SourceTitle = "凡人修仙传", SourceAuthor = "忘语",
                ProfileJson = "稳健", Tags = "[\"仙侠\"]" }
        );
        await db.SaveChangesAsync();

        // 用完
        var (p1, _) = await injector.SelectRandomStyleAsync(1, 1);
        Assert.NotNull(p1);

        // 同卷耗尽
        var (p2, _) = await injector.SelectRandomStyleAsync(1, 2);
        Assert.Null(p2);

        // 新卷恢复
        var (p3, _) = await injector.SelectRandomStyleAsync(2, 1);
        Assert.NotNull(p3);
    }
}

// ============================================================
// 插曲库: 按项目隔离 + 卷间独立
// ============================================================
public class InterludeLibraryPerProjectTests
{
    [Fact]
    public async Task InterludeLibrary_ProjectIsolation()
    {
        await using var db = TestDbHelper.CreateInMemoryDb();
        var repo = new InterludeRepository(db);
        var pidA = ProjectId.New();
        var pidB = ProjectId.New();

        db.InterludeEntries.AddRange(
            new InterludeEntry { Id = "E1", SourceType = "historical", Source = "史记",
                CoreFact = "荆轲刺秦", NarrativeHook = "赴死", AdaptableThemes = "[\"忠义\"]" },
            new InterludeEntry { Id = "E2", SourceType = "anecdote", Source = "庄子",
                CoreFact = "庖丁解牛", NarrativeHook = "化境", AdaptableThemes = "[\"修炼\"]" }
        );
        await db.SaveChangesAsync();

        // 项目A用E1
        await repo.LogUsageAsync("E1", pidA, 1, 1);
        await db.SaveChangesAsync();

        // 项目B不受影响
        var b = await repo.GetRandomUnusedInVolumeAsync(pidB, 1);
        Assert.NotNull(b);

        // 项目A只剩E2
        var a = await repo.GetRandomUnusedInVolumeAsync(pidA, 1);
        Assert.NotNull(a);
        Assert.Equal("E2", a!.Id);
    }

    [Fact]
    public async Task InterludeLibrary_SeparateVolumes_Independent()
    {
        await using var db = TestDbHelper.CreateInMemoryDb();
        var repo = new InterludeRepository(db);
        var pid = ProjectId.New();

        db.InterludeEntries.AddRange(
            new InterludeEntry { Id = "E1", SourceType = "historical", Source = "史记",
                CoreFact = "完璧归赵", NarrativeHook = "智勇", AdaptableThemes = "[\"谋略\"]" },
            new InterludeEntry { Id = "E2", SourceType = "anecdote", Source = "世说",
                CoreFact = "雪夜访戴", NarrativeHook = "随性", AdaptableThemes = "[\"洒脱\"]" }
        );
        await db.SaveChangesAsync();

        // 卷1用E1
        await repo.LogUsageAsync("E1", pid, 1, 2);
        await db.SaveChangesAsync();

        // 卷2仍然可用E1
        var v2 = await repo.GetRandomUnusedInVolumeAsync(pid, 2);
        Assert.NotNull(v2);
    }
}

// ============================================================
// 导入器: 从本地目录扫描文章
// ============================================================
public class StyleImporterTests
{
    [Fact]
    public async Task ImportFromDirectory_WithTxtFiles_ImportsCorrectCount()
    {
        var dir = CreateTempDir();
        try
        {
            File.WriteAllText(Path.Combine(dir, "斗破苍穹_天蚕土豆.txt"), CreateLongText("废材逆袭", 500));
            File.WriteAllText(Path.Combine(dir, "诡秘之主_乌贼.txt"), CreateLongText("悬疑克苏鲁", 500));

            await using var db = TestDbHelper.CreateInMemoryDb();
            var repo = new StyleLibraryRepository(db);

            // Mock LLM: 返回预设的风格档案
            var mockLlm = new MockStyleLlm();
            var agent = new StyleExtractionAgent(mockLlm, repo);
            var importer = new StyleImporter(agent, repo);

            var result = await importer.ImportFromDirectoryAsync(dir);

            Assert.Null(result.Error);
            Assert.Equal(2, result.Imported.Count);
            Assert.Empty(result.Failed);
        }
        finally { Directory.Delete(dir, true); }
    }

    [Fact]
    public async Task ImportFromDirectory_EmptyDir_ReturnsError()
    {
        var dir = CreateTempDir();
        try
        {
            await using var db = TestDbHelper.CreateInMemoryDb();
            var repo = new StyleLibraryRepository(db);
            var mockLlm = new MockStyleLlm();
            var agent = new StyleExtractionAgent(mockLlm, repo);
            var importer = new StyleImporter(agent, repo);

            var result = await importer.ImportFromDirectoryAsync(dir);
            Assert.NotNull(result.Error);
            Assert.Contains("无 .txt 文件", result.Error);
        }
        finally { Directory.Delete(dir, true); }
    }

    [Fact]
    public async Task ImportFromDirectory_NonExistentDir_ReturnsError()
    {
        await using var db = TestDbHelper.CreateInMemoryDb();
        var repo = new StyleLibraryRepository(db);
        var mockLlm = new MockStyleLlm();
        var agent = new StyleExtractionAgent(mockLlm, repo);
        var importer = new StyleImporter(agent, repo);

        var result = await importer.ImportFromDirectoryAsync("C:\\nonexistent_dir_12345");
        Assert.NotNull(result.Error);
    }

    private static string CreateTempDir()
    {
        var dir = Path.Combine(Path.GetTempPath(), $"StyleTest_{Guid.NewGuid():N}");
        Directory.CreateDirectory(dir);
        return dir;
    }

    private static string CreateLongText(string theme, int minChars)
    {
        var sb = new System.Text.StringBuilder();
        while (sb.Length < minChars)
        {
            sb.AppendLine($"这是一个关于{theme}的测试故事。故事发生在一个虚构的世界中。");
            sb.AppendLine("主角面临着巨大的挑战，但他从未放弃。");
            sb.AppendLine("每一次战斗都让他变得更加强大，每一次失败都让他更加坚定。");
        }
        return sb.ToString();
    }

    /// <summary>
    /// Mock LLM 适配器，始终返回预设的风格档案 JSON。
    /// </summary>
    private class MockStyleLlm : ILlmAdapter
    {
        public string ModelName => "mock";
        public int MaxContextTokens => 1000;
        public int RecommendedOutputTokens => 100;

        public Task<string> ChatAsync(string systemPrompt, string userMessage, CancellationToken ct)
            => Task.FromResult(
                """{"sentence_patterns":"短句为主","lexical_preferences":"战斗词汇密集","rhetorical_habits":"比喻","narrative_distance":"全知视角","paragraph_rhythm":"快节奏"}""");

        public Task<string> ChatAsync(IEnumerable<NovelWriter.Core.Dtos.ChatMessage> messages, CancellationToken ct)
            => Task.FromResult(
                """{"sentence_patterns":"短句为主","lexical_preferences":"战斗词汇密集","rhetorical_habits":"比喻","narrative_distance":"全知视角","paragraph_rhythm":"快节奏"}""");

        public IAsyncEnumerable<string> StreamChatAsync(string systemPrompt, string userMessage, CancellationToken ct)
            => throw new NotSupportedException();
    }
}

public static class TestDbHelper
{
    public static NovelWriterDbContext CreateInMemoryDb()
    {
        var options = new DbContextOptionsBuilder<NovelWriterDbContext>()
            .UseInMemoryDatabase($"test_{Guid.NewGuid():N}")
            .Options;
        var db = new NovelWriterDbContext(options);
        db.Database.EnsureCreated();
        return db;
    }
}
