using System.IO;
using System.Windows;
using System.Windows.Controls;
using NovelWriter.Core.Entities;
using NovelWriter.Core.Enums;
using NovelWriter.Core.ValueObjects;

namespace NovelWriter.App.Views;

public partial class NewProjectDialog : Window
{
    public Project? CreatedProject { get; private set; }
    public string StoryIdea => IdeaBox.Text;
    public string Genre => ((ComboBoxItem)GenreCombo.SelectedItem).Content.ToString()!;

    public NewProjectDialog()
    {
        InitializeComponent();
        IdeaBox.TextChanged += (s, e) =>
            CharCount.Text = $"{IdeaBox.Text.Length}/200";
    }

    private void Create_Click(object sender, RoutedEventArgs e)
    {
        var title = TitleBox.Text.Trim();
        if (string.IsNullOrWhiteSpace(title))
        {
            ErrorText.Text = "请输入书名";
            return;
        }

        CreatedProject = new Project
        {
            Id = ProjectId.New(),
            Title = title,
            Genre = Genre,
            Status = ProjectStatus.Active,
            StoryIdea = IdeaBox.Text.Trim()
        };

        // 创建项目专属目录
        var projectDir = GetProjectDir(CreatedProject.Id);
        Directory.CreateDirectory(projectDir);
        Directory.CreateDirectory(Path.Combine(projectDir, "chapters"));
        Directory.CreateDirectory(Path.Combine(projectDir, "backups"));

        DialogResult = true;
        Close();
    }

    private void Cancel_Click(object sender, RoutedEventArgs e)
    {
        DialogResult = false;
        Close();
    }

    public static string GetProjectDir(ProjectId id)
    {
        var baseDir = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "NovelWriter", "Projects", id.Value.ToString("N"));
        return baseDir;
    }

    public static string GetProjectDbPath(ProjectId id)
        => Path.Combine(GetProjectDir(id), "project.db");
}
