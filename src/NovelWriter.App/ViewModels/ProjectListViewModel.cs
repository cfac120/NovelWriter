using System.Collections.ObjectModel;
using System.Windows;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using NovelWriter.Storage;

namespace NovelWriter.App.ViewModels;

public partial class ProjectListViewModel : ViewModelBase
{
    [ObservableProperty]
    private ObservableCollection<ProjectItem> _projects = [];

    [ObservableProperty]
    private string _newProjectTitle = string.Empty;

    [ObservableProperty]
    private bool _isLoading;

    [RelayCommand]
    private async Task LoadProjects()
    {
        IsLoading = true;
        try
        {
            await using var scope = NovelWriterApp.Services.CreateAsyncScope();
            var db = scope.ServiceProvider.GetRequiredService<NovelWriterDbContext>();
            var list = await db.Projects.OrderByDescending(p => p.CreatedAt).ToListAsync();
            Projects = new ObservableCollection<ProjectItem>(
                list.Select(p => new ProjectItem
                {
                    Id = p.Id.Value.ToString(),
                    Title = p.Title,
                    Genre = p.Genre ?? "",
                    ChapterCount = db.Chapters.Count(c => c.ProjectId == p.Id)
                }));
        }
        finally { IsLoading = false; }
    }

    [RelayCommand]
    private void OpenProject(string projectId)
    {
        if (Application.Current.Windows.OfType<Views.ShellWindow>()
            .FirstOrDefault()?.DataContext is ShellViewModel svm)
        {
            var editor = new EditorViewModel
            {
                ChapterTitle = "加载中..."
            };
            svm.NavigateToCommand.Execute("editor");
            svm.StatusText = $"已打开项目: {projectId}";
        }
    }

    [RelayCommand]
    private async Task CreateProject()
    {
        if (string.IsNullOrWhiteSpace(NewProjectTitle)) return;
        await using var scope = NovelWriterApp.Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<NovelWriterDbContext>();
        var project = new Core.Entities.Project
        {
            Title = NewProjectTitle,
            Genre = "未分类",
            Status = Core.Enums.ProjectStatus.Active
        };
        db.Projects.Add(project);
        await db.SaveChangesAsync();

        var pid = project.Id;
        NewProjectTitle = string.Empty;
        await LoadProjects();

        // 弹出项目设置向导
        var owner = Application.Current.Windows.OfType<Window>().FirstOrDefault(w => w.IsActive);
        var setup = new Views.ProjectSetupDialog(pid) { Owner = owner };
        setup.ShowDialog();

        if (setup.SetupCompleted)
        {
            // 打开编辑器
            OpenProjectCommand.Execute(pid.Value.ToString());
        }
    }

    public record ProjectItem
    {
        public string Id { get; init; } = "";
        public string Title { get; init; } = "";
        public string Genre { get; init; } = "";
        public int ChapterCount { get; init; }
    }
}
