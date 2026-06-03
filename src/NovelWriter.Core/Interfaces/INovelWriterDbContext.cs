using NovelWriter.Core.Entities;
using NovelWriter.Core.Memory;

namespace NovelWriter.Core.Interfaces;

/// <summary>
/// Core 层定义的 DbContext 抽象接口，不含 EF Core 依赖。
/// Storage 层的 NovelWriterDbContext 实现此接口。
/// </summary>
public interface INovelWriterDbContext
{
    // Core 层只定义保存方法，DbSet 属性由 Storage 层的具体实现暴露
    Task<int> SaveChangesAsync(CancellationToken ct = default);
}
