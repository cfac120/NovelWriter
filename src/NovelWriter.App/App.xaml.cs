using System.Windows;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using NovelWriter.Core.Interfaces;
using NovelWriter.Engine.ContextWindow;
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

    protected override void OnStartup(StartupEventArgs e)
    {
        base.OnStartup(e);

        try
        {
            var services = new ServiceCollection();

            services.AddDbContext<NovelWriterDbContext>(options =>
                options.UseSqlite("Data Source=novelwriter.db"));
            services.AddScoped<INovelWriterDbContext>(sp => sp.GetRequiredService<NovelWriterDbContext>());
            services.AddScoped<IProjectRepository, ProjectRepository>();
            services.AddScoped<IChapterRepository, ChapterRepository>();
            services.AddScoped<IOutlineRepository, OutlineRepository>();
            services.AddScoped<IMemoryRepository, MemoryRepository>();

            services.AddSingleton<TokenCounter>();
            services.AddScoped<ContextWindowCompiler>();
            services.AddScoped<L2Updater>();
            services.AddScoped<MemoryChangeValidator>();
            services.AddScoped<ReviewAggregator>();

            services.AddSingleton<ShellViewModel>();
            services.AddSingleton<ShellWindow>();

            Services = services.BuildServiceProvider();

            using var scope = Services.CreateScope();
            var db = scope.ServiceProvider.GetRequiredService<NovelWriterDbContext>();
            db.Database.EnsureCreated();

            var shell = Services.GetRequiredService<ShellWindow>();
            MainWindow = shell;
            shell.Show();
        }
        catch (Exception ex)
        {
            MessageBox.Show($"启动失败: {ex}", "NovelWriter",
                MessageBoxButton.OK, MessageBoxImage.Error);
            Shutdown();
        }
    }
}
