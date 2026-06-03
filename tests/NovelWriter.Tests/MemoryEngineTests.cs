using Microsoft.EntityFrameworkCore;
using NovelWriter.Core.Dtos;
using NovelWriter.Core.Entities;
using NovelWriter.Core.Enums;
using NovelWriter.Core.Memory;
using NovelWriter.Core.ValueObjects;
using NovelWriter.Engine.Memory;
using NovelWriter.Storage;
using NovelWriter.Storage.Repositories;
using Moq;

namespace NovelWriter.Tests;

public class L3RetrieverTests : IDisposable
{
    private readonly NovelWriterDbContext _db;
    private readonly MemoryRepository _repo;
    private readonly ProjectId _projectId;

    public L3RetrieverTests()
    {
        var options = new DbContextOptionsBuilder<NovelWriterDbContext>()
            .UseSqlite("Data Source=:memory:")
            .Options;
        _db = new NovelWriterDbContext(options);
        _db.Database.OpenConnection();
        _db.Database.EnsureCreated();
        _repo = new MemoryRepository(_db);
        _projectId = ProjectId.New();
    }

    [Fact]
    public async Task Retrieve_WithNoData_ReturnsEmpty()
    {
        var retriever = new L3Retriever(_repo);
        var outline = new Outline { ChapterNumber = 1, VolumeNumber = 1, SceneDescription = "测试场景" };
        var result = await retriever.RetrieveAsync(_projectId, outline, 1, CancellationToken.None);
        Assert.NotNull(result);
    }

    [Fact]
    public async Task Retrieve_WithCharacterData_ReturnsProfile()
    {
        var charId = new CharacterId(1);
        await _repo.AddCharacterProfileVersionAsync(new CharacterProfile
        {
            CharacterId = charId,
            ProjectId = _projectId,
            Name = "测试角色",
            Profile = "核心特质: 勇敢, 聪明\n禁止特质: 懦弱"
        });

        var retriever = new L3Retriever(_repo);
        var outline = new Outline
        {
            ChapterNumber = 5,
            VolumeNumber = 1,
            SceneDescription = "测试场景",
            CharacterInvolvement = "CHAR_001"
        };

        var result = await retriever.RetrieveAsync(_projectId, outline, 5, CancellationToken.None);
        Assert.Contains("测试角色", result);
    }

    public void Dispose()
    {
        _db.Database.CloseConnection();
        _db.Dispose();
    }
}

public class L2ToL3CompressorTests : IDisposable
{
    private readonly NovelWriterDbContext _db;
    private readonly MemoryRepository _repo;
    private readonly ProjectId _projectId;

    public L2ToL3CompressorTests()
    {
        var options = new DbContextOptionsBuilder<NovelWriterDbContext>()
            .UseSqlite("Data Source=:memory:")
            .Options;
        _db = new NovelWriterDbContext(options);
        _db.Database.OpenConnection();
        _db.Database.EnsureCreated();
        _repo = new MemoryRepository(_db);
        _projectId = ProjectId.New();
    }

    [Fact]
    public async Task CompressVolume_WithResolvedForeshadowing_ArchivesIt()
    {
        await _repo.AddForeshadowingAsync(new Foreshadowing
        {
            ForeshadowingId = new ForeshadowingId(1),
            ProjectId = _projectId,
            VolumeNumber = 1,
            Description = "测试伏笔",
            Status = ForeshadowingStatus.Resolved,
            Priority = ForeshadowingPriority.High,
            ResolvedChapter = 5
        });

        var compressor = new L2ToL3Compressor(_repo);
        var report = await compressor.CompressVolumeAsync(_projectId, 1, CancellationToken.None);

        Assert.Equal(1, report.VolumeNumber);
        Assert.True(report.OriginalTokenCount > 0);
    }

    public void Dispose()
    {
        _db.Database.CloseConnection();
        _db.Dispose();
    }
}

public class MemoryChangeValidatorTests
{
    [Fact]
    public async Task Validate_RemovesInvalidReferences()
    {
        var mockRepo = new Mock<Core.Interfaces.IMemoryRepository>();
        mockRepo.Setup(r => r.GetActiveForeshadowingsAsync(It.IsAny<ProjectId>(), It.IsAny<int>()))
            .ReturnsAsync([]);
        mockRepo.Setup(r => r.GetArcTrackersAsync(It.IsAny<ProjectId>()))
            .ReturnsAsync([]);
        mockRepo.Setup(r => r.GetSubplotTrackersAsync(It.IsAny<ProjectId>(), It.IsAny<int>()))
            .ReturnsAsync([]);
        mockRepo.Setup(r => r.GetCharacterProfilesAsync(It.IsAny<ProjectId>()))
            .ReturnsAsync([]);
        mockRepo.Setup(r => r.GetWorldSettingsAsync(It.IsAny<ProjectId>()))
            .ReturnsAsync([]);

        var validator = new MemoryChangeValidator(mockRepo.Object);
        var extraction = new MemoryExtractionResult
        {
            L1Summary = new ChapterSummary { ChapterNumber = 1 },
            ForeshadowingResolutions = [new ForeshadowingResolution { ForeshadowingId = "FS_999" }],
            L3ChangeProposals = [new L3ChangeProposal { TargetId = "CHAR_999" }]
        };

        var result = await validator.ValidateAndFilterAsync(
            extraction, ProjectId.New(), 1, CancellationToken.None);

        Assert.Empty(result.ForeshadowingResolutions);
        Assert.Empty(result.L3ChangeProposals);
    }
}
