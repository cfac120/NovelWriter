using System.Net.Http;
using System.Windows;
using System.Windows.Threading;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using NovelWriter.Core.Interfaces;
using NovelWriter.Engine.Llm;
using NovelWriter.Engine.Pipeline;
using NovelWriter.Storage;
using NovelWriter.Storage.Repositories;
using NovelWriter.App.ViewModels;
using NovelWriter.App.Views;

namespace NovelWriter.App;

public partial class NovelWriterApp : Application
{
    public static ServiceProvider Services { get; private set; } = null!;

    /// <summary>
    /// 全局共享的 LLM 运行时配置。ShellWindow 在保存配置时调用
    /// <c>LlmConfig.Update(...)</c>，所有已构造的 ILlmAdapter 立即生效。
    /// </summary>
    public static LlmRuntimeConfig LlmConfig { get; } = new(
        apiKey: Environment.GetEnvironmentVariable("LLM_API_KEY") ?? "",
        model:  Environment.GetEnvironmentVariable("LLM_MODEL") ?? "",
        endpoint: Environment.GetEnvironmentVariable("LLM_BASE_URL") ?? ""
    );

    public NovelWriterApp()
    {
        DispatcherUnhandledException += (s, e) =>
        {
            MessageBox.Show($"未处理异常:\n{e.Exception}", "NovelWriter 错误",
                MessageBoxButton.OK, MessageBoxImage.Error);
            e.Handled = true;
        };
    }

    protected override void OnStartup(StartupEventArgs e)
    {
        base.OnStartup(e);

        try
        {
            var services = new ServiceCollection();
            services.AddDbContext<NovelWriterDbContext>(o =>
                o.UseSqlite("Data Source=novelwriter.db"));
            services.AddScoped<INovelWriterDbContext>(sp => sp.GetRequiredService<NovelWriterDbContext>());
            services.AddScoped<IProjectRepository, ProjectRepository>();
            services.AddScoped<IChapterRepository, ChapterRepository>();
            services.AddScoped<IOutlineRepository, OutlineRepository>();
            services.AddScoped<IMemoryRepository, MemoryRepository>();
            services.AddScoped<IStyleLibraryRepository, StyleLibraryRepository>();
            services.AddScoped<IInterludeRepository, InterludeRepository>();

            // 共享 LLM 运行时配置
            services.AddSingleton(LlmConfig);

            // ILlmAdapter 单例：所有生成器、Agent 共享同一个适配器实例。
            // 适配器持有 LlmRuntimeConfig 引用，配置更新时无需重建。
            services.AddSingleton<ILlmAdapter>(_ =>
            {
                var http = new HttpClient { Timeout = TimeSpan.FromMinutes(10) };
                return new GenericOpenAiAdapter(http, LlmConfig);
            });

            services.AddScoped<SynopsisGenerator>();
            services.AddScoped<OutlineGenerator>();

            services.AddSingleton<ShellViewModel>();
            services.AddTransient<ShellWindow>();

            Services = services.BuildServiceProvider();

            using (var scope = Services.CreateScope())
            {
                var db = scope.ServiceProvider.GetRequiredService<NovelWriterDbContext>();
                db.Database.EnsureCreated();
            }

            var shell = new ShellWindow();
            MainWindow = shell;
            shell.Show();
        }
        catch (Exception ex)
        {
            MessageBox.Show($"启动失败:\n{ex}", "NovelWriter 错误",
                MessageBoxButton.OK, MessageBoxImage.Error);
            Shutdown();
        }
    }
}
