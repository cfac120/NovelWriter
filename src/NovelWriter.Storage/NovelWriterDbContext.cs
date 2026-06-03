using Microsoft.EntityFrameworkCore;
using NovelWriter.Core.Entities;
using NovelWriter.Core.Interfaces;
using NovelWriter.Core.Memory;

namespace NovelWriter.Storage;

public class NovelWriterDbContext : DbContext, INovelWriterDbContext
{
    public DbSet<Project> Projects => Set<Project>();
    public DbSet<Chapter> Chapters => Set<Chapter>();
    public DbSet<Outline> Outlines => Set<Outline>();
    public DbSet<Foreshadowing> Foreshadowings => Set<Foreshadowing>();
    public DbSet<ArcTracker> ArcTrackers => Set<ArcTracker>();
    public DbSet<SubplotTracker> SubplotTrackers => Set<SubplotTracker>();
    public DbSet<CharacterProfile> CharacterProfiles => Set<CharacterProfile>();
    public DbSet<WorldSetting> WorldSettings => Set<WorldSetting>();
    public DbSet<ChapterSummary> ChapterSummaries => Set<ChapterSummary>();
    public DbSet<ForeshadowingArchive> ForeshadowingArchives => Set<ForeshadowingArchive>();
    public DbSet<VolumeSummary> VolumeSummaries => Set<VolumeSummary>();
    public DbSet<Synopsis> Synopses => Set<Synopsis>();
    public DbSet<Review> Reviews => Set<Review>();
    public DbSet<Persona> Personas => Set<Persona>();
    public DbSet<StyleProfile> StyleProfiles => Set<StyleProfile>();
    public DbSet<StyleUsageLog> StyleUsageLogs => Set<StyleUsageLog>();
    public DbSet<InterludeEntry> InterludeEntries => Set<InterludeEntry>();
    public DbSet<InterludeUsageLog> InterludeUsageLogs => Set<InterludeUsageLog>();

    public NovelWriterDbContext(DbContextOptions<NovelWriterDbContext> options) : base(options) { }

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        modelBuilder.Entity<Project>(e =>
        {
            e.HasKey(p => p.Id);
            e.Property(p => p.Id).HasConversion(ValueConverters.ProjectIdConverter, ValueConverters.ProjectIdComparer);
            e.Property(p => p.Title).IsRequired().HasMaxLength(200);
        });

        modelBuilder.Entity<Chapter>(e =>
        {
            e.HasKey(c => c.Id);
            e.Property(c => c.Id).HasConversion(ValueConverters.ChapterIdConverter, ValueConverters.ChapterIdComparer);
            e.Property(c => c.ProjectId).HasConversion(ValueConverters.ProjectIdConverter, ValueConverters.ProjectIdComparer);
            e.HasIndex(c => new { c.ProjectId, c.VolumeNumber, c.ChapterNumber }).IsUnique();
        });

        modelBuilder.Entity<Outline>(e =>
        {
            e.HasKey(o => o.Id);
            e.Property(o => o.ProjectId).HasConversion(ValueConverters.ProjectIdConverter, ValueConverters.ProjectIdComparer);
            e.HasIndex(o => new { o.ProjectId, o.VolumeNumber, o.ChapterNumber }).IsUnique();
        });

        modelBuilder.Entity<Foreshadowing>(e =>
        {
            e.HasKey(f => f.Id);
            e.Property(f => f.ProjectId).HasConversion(ValueConverters.ProjectIdConverter, ValueConverters.ProjectIdComparer);
            e.Property(f => f.ForeshadowingId).HasConversion(ValueConverters.ForeshadowingIdConverter, ValueConverters.ForeshadowingIdComparer);
            e.HasIndex(f => new { f.ProjectId, f.VolumeNumber });
        });

        modelBuilder.Entity<ArcTracker>(e =>
        {
            e.HasKey(a => a.Id);
            e.Property(a => a.ProjectId).HasConversion(ValueConverters.ProjectIdConverter, ValueConverters.ProjectIdComparer);
            e.Property(a => a.ArcId).HasConversion(ValueConverters.ArcIdConverter, ValueConverters.ArcIdComparer);
        });

        modelBuilder.Entity<SubplotTracker>(e =>
        {
            e.HasKey(s => s.Id);
            e.Property(s => s.ProjectId).HasConversion(ValueConverters.ProjectIdConverter, ValueConverters.ProjectIdComparer);
        });

        modelBuilder.Entity<CharacterProfile>(e =>
        {
            e.HasKey(c => c.Id);
            e.Property(c => c.ProjectId).HasConversion(ValueConverters.ProjectIdConverter, ValueConverters.ProjectIdComparer);
            e.Property(c => c.CharacterId).HasConversion(ValueConverters.CharacterIdConverter, ValueConverters.CharacterIdComparer);
            e.HasIndex(c => new { c.CharacterId, c.Version });
        });

        modelBuilder.Entity<WorldSetting>(e =>
        {
            e.HasKey(w => w.Id);
            e.Property(w => w.ProjectId).HasConversion(ValueConverters.ProjectIdConverter, ValueConverters.ProjectIdComparer);
            e.Property(w => w.WorldSettingId).HasConversion(ValueConverters.WorldSettingIdConverter, ValueConverters.WorldSettingIdComparer);
            e.HasIndex(w => new { w.WorldSettingId, w.Version });
        });

        modelBuilder.Entity<ChapterSummary>(e =>
        {
            e.HasKey(c => c.Id);
            e.Property(c => c.ProjectId).HasConversion(ValueConverters.ProjectIdConverter, ValueConverters.ProjectIdComparer);
            e.HasIndex(c => new { c.ProjectId, c.VolumeNumber, c.ChapterNumber }).IsUnique();
        });

        modelBuilder.Entity<ForeshadowingArchive>(e =>
        {
            e.HasKey(f => f.Id);
            e.Property(f => f.ProjectId).HasConversion(ValueConverters.ProjectIdConverter, ValueConverters.ProjectIdComparer);
            e.Property(f => f.ForeshadowingId).HasConversion(ValueConverters.ForeshadowingIdConverter, ValueConverters.ForeshadowingIdComparer);
        });

        modelBuilder.Entity<VolumeSummary>(e =>
        {
            e.HasKey(v => v.Id);
            e.Property(v => v.ProjectId).HasConversion(ValueConverters.ProjectIdConverter, ValueConverters.ProjectIdComparer);
            e.HasIndex(v => new { v.ProjectId, v.VolumeNumber }).IsUnique();
        });

        modelBuilder.Entity<Synopsis>(e =>
        {
            e.HasKey(s => s.Id);
            e.Property(s => s.ProjectId).HasConversion(ValueConverters.ProjectIdConverter, ValueConverters.ProjectIdComparer);
        });

        modelBuilder.Entity<Review>(e =>
        {
            e.HasKey(r => r.Id);
            e.Property(r => r.ChapterId).HasConversion(ValueConverters.ChapterIdConverter, ValueConverters.ChapterIdComparer);
        });

        modelBuilder.Entity<StyleUsageLog>(e =>
        {
            e.HasKey(s => s.Id);
        });

        modelBuilder.Entity<InterludeUsageLog>(e =>
        {
            e.HasKey(i => i.Id);
            e.Property(i => i.ProjectId).HasConversion(ValueConverters.ProjectIdConverter, ValueConverters.ProjectIdComparer);
        });

        modelBuilder.Entity<Persona>(e => e.HasKey(p => p.Id));
        modelBuilder.Entity<StyleProfile>(e => e.HasKey(s => s.Id));
        modelBuilder.Entity<InterludeEntry>(e => e.HasKey(i => i.Id));
    }
}
