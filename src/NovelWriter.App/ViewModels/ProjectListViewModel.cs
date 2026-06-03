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
            svm.NavigateToCommand.Execute("editor");
            svm.StatusText = $"已打开项目";
        }
    }

    [RelayCommand]
    private async Task CreateProject()
    {
        var owner = Application.Current.Windows.OfType<Window>().FirstOrDefault(w => w.IsActive);
        var newProjDlg = new Views.NewProjectDialog { Owner = owner };
        if (newProjDlg.ShowDialog() != true || newProjDlg.CreatedProject == null)
            return;

        var project = newProjDlg.CreatedProject;

        await using var scope = NovelWriterApp.Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<NovelWriterDbContext>();
        db.Projects.Add(project);
        await db.SaveChangesAsync();
        await LoadProjects();

        // 打开项目设置向导(梗概+大纲)
        var setupDlg = new Views.ProjectSetupDialog(
            project.Id, project.Genre ?? "玄幻",
            newProjDlg.StoryIdea, newProjDlg.StyleEnabled, newProjDlg.InterludeEnabled)
        { Owner = owner };
        setupDlg.ShowDialog();

        if (setupDlg.SetupCompleted)
            OpenProjectCommand.Execute(project.Id.Value.ToString());
    }

    public record ProjectItem
    {
        public string Id { get; init; } = "";
        public string Title { get; init; } = "";
        public string Genre { get; init; } = "";
        public int ChapterCount { get; init; }
    }
}
