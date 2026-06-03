using System.Windows;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using NovelWriter.Core.Interfaces;
using NovelWriter.Engine.ContextWindow;
using NovelWriter.Engine.Llm;
using NovelWriter.Engine.Memory;
using NovelWriter.Engine.Review;
using NovelWriter.Storage;
using NovelWriter.Storage.Repositories;
using NovelWriter.App.ViewModels;
using NovelWriter.App.Views;

namespace NovelWriter.App;

public partial class NovelWriterApp : Application
{
    public static ServiceProvider Services { get; private set; } = null!;

    private void OnStartup(object sender, StartupEventArgs e)
    {
        var services = new ServiceCollection();

        // Storage
        services.AddDbContext<NovelWriterDbContext>(options =>
            options.UseSqlite("Data Source=novelwriter.db"));
        services.AddScoped<INovelWriterDbContext>(sp => sp.GetRequiredService<NovelWriterDbContext>());
        services.AddScoped<IProjectRepository, ProjectRepository>();
        services.AddScoped<IChapterRepository, ChapterRepository>();
        services.AddScoped<IOutlineRepository, OutlineRepository>();
        services.AddScoped<IMemoryRepository, MemoryRepository>();

        // Engine
        services.AddSingleton<TokenCounter>();
        services.AddScoped<ContextWindowCompiler>();
        services.AddScoped<L2Updater>();
        services.AddScoped<MemoryChangeValidator>();
        services.AddScoped<ReviewAggregator>();

        // ViewModels
        services.AddSingleton<ShellViewModel>();
        services.AddTransient<EditorViewModel>();
        services.AddTransient<ProjectListViewModel>();

        // Views
        services.AddSingleton<ShellWindow>();

        Services = services.BuildServiceProvider();

        // 确保数据库创建
        using var scope = Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<NovelWriterDbContext>();
        db.Database.EnsureCreated();

        var shell = Services.GetRequiredService<ShellWindow>();
        shell.Show();
    }
}
