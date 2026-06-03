using Microsoft.EntityFrameworkCore;
using NovelWriter.Core.Entities;
using NovelWriter.Core.Enums;
using NovelWriter.Core.Memory;
using NovelWriter.Core.ValueObjects;
using NovelWriter.Storage;
using NovelWriter.Storage.Repositories;
using Xunit;

namespace NovelWriter.Tests;

public class StorageIntegrationTests : IDisposable
{
    private readonly NovelWriterDbContext _db;
    private readonly ProjectRepository _projectRepo;
    private readonly ChapterRepository _chapterRepo;

    public StorageIntegrationTests()
    {
        var options = new DbContextOptionsBuilder<NovelWriterDbContext>()
            .UseSqlite("Data Source=:memory:")
            .Options;

        _db = new NovelWriterDbContext(options);
        _db.Database.OpenConnection();
        _db.Database.EnsureCreated();

        _projectRepo = new ProjectRepository(_db);
        _chapterRepo = new ChapterRepository(_db);
    }

    [Fact]
    public async Task ProjectRepository_AddAndGetById_Works()
    {
        var project = new Project
        {
            Title = "测试小说",
            Genre = "仙侠",
            Status = ProjectStatus.Active,
            TargetWordCount = 1000000,
            TargetChapterCount = 300
        };

        await _projectRepo.AddAsync(project);
        var retrieved = await _projectRepo.GetByIdAsync(project.Id);

        Assert.NotNull(retrieved);
        Assert.Equal("测试小说", retrieved.Title);
        Assert.Equal(ProjectStatus.Active, retrieved.Status);
    }

    [Fact]
    public async Task ProjectRepository_GetAll_ReturnsAllProjects()
    {
        await _projectRepo.AddAsync(new Project { Title = "小说1" });
        await _projectRepo.AddAsync(new Project { Title = "小说2" });

        var all = await _projectRepo.GetAllAsync();
        Assert.Equal(2, all.Count);
    }

    [Fact]
    public async Task ChapterRepository_AddAndGetByProject_Works()
    {
        var projectId = ProjectId.New();
        var project = new Project { Id = projectId, Title = "测试" };
        await _projectRepo.AddAsync(project);

        await _chapterRepo.AddAsync(new Chapter
        {
            ProjectId = projectId,
            VolumeNumber = 1,
            ChapterNumber = 1,
            Title = "第一章",
            Content = "这是第一章的内容",
            WordCount = 3000
        });

        await _chapterRepo.AddAsync(new Chapter
        {
            ProjectId = projectId,
            VolumeNumber = 1,
            ChapterNumber = 2,
            Title = "第二章",
            Content = "这是第二章的内容",
            WordCount = 3500
        });

        var chapters = await _chapterRepo.GetByProjectAsync(projectId);
        Assert.Equal(2, chapters.Count);
    }

    [Fact]
    public async Task ChapterRepository_GetByNumber_ReturnsCorrectChapter()
    {
        var projectId = ProjectId.New();
        await _projectRepo.AddAsync(new Project { Id = projectId, Title = "测试" });

        await _chapterRepo.AddAsync(new Chapter
        {
            ProjectId = projectId,
            VolumeNumber = 1,
            ChapterNumber = 5,
            Title = "第五章"
        });

        var chapter = await _chapterRepo.GetByNumberAsync(projectId, 1, 5);
        Assert.NotNull(chapter);
        Assert.Equal("第五章", chapter.Title);
    }

    [Fact]
    public async Task MemoryRepository_CharacterProfile_Works()
    {
        var projectId = ProjectId.New();
        await _projectRepo.AddAsync(new Project { Id = projectId, Title = "测试" });

        var memoryRepo = new MemoryRepository(_db);
        var charId = new CharacterId(1);

        await memoryRepo.AddCharacterProfileVersionAsync(new CharacterProfile
        {
            CharacterId = charId,
            ProjectId = projectId,
            Name = "张三",
            Profile = "主角，修仙者",
            Version = 1
        });

        var profiles = await memoryRepo.GetCharacterProfilesAsync(projectId);
        Assert.Single(profiles);
        Assert.Equal("张三", profiles[0].Name);

        var latest = await memoryRepo.GetLatestCharacterProfileAsync(charId);
        Assert.NotNull(latest);
        Assert.Equal(1, latest.Version);
    }

    [Fact]
    public async Task MemoryRepository_Foreshadowing_Works()
    {
        var projectId = ProjectId.New();
        await _projectRepo.AddAsync(new Project { Id = projectId, Title = "测试" });

        var memoryRepo = new MemoryRepository(_db);

        await memoryRepo.AddForeshadowingAsync(new Foreshadowing
        {
            ForeshadowingId = new ForeshadowingId(1),
            ProjectId = projectId,
            VolumeNumber = 1,
            Description = "神秘的玉佩",
            Status = ForeshadowingStatus.Active,
            Priority = ForeshadowingPriority.High
        });

        var active = await memoryRepo.GetActiveForeshadowingsAsync(projectId, 1);
        Assert.Single(active);
        Assert.Equal("神秘的玉佩", active[0].Description);
    }

    [Fact]
    public async Task MemoryRepository_ChapterSummary_Works()
    {
        var projectId = ProjectId.New();
        await _projectRepo.AddAsync(new Project { Id = projectId, Title = "测试" });

        var memoryRepo = new MemoryRepository(_db);

        for (int i = 1; i <= 5; i++)
        {
            await memoryRepo.AddChapterSummaryAsync(new ChapterSummary
            {
                ProjectId = projectId,
                VolumeNumber = 1,
                ChapterNumber = i,
                Summary = $"第{i}章摘要",
                TokenCount = 200
            });
        }

        var recent = await memoryRepo.GetRecentSummariesAsync(projectId, 3);
        Assert.Equal(3, recent.Count);
        // Should return chapters 5, 4, 3 (most recent first)
        Assert.Equal(5, recent[0].ChapterNumber);
    }

    public void Dispose()
    {
        _db.Database.CloseConnection();
        _db.Dispose();
    }
}
