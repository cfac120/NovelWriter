using System.IO;
using System.Text.Json;
using System.Windows;

namespace NovelWriter.App.Views;

public partial class OpenProjectDialog : Window
{
    public string? SelectedPath { get; private set; }
    private readonly string _projectsRoot;

    public OpenProjectDialog(string projectsRoot)
    {
        _projectsRoot = projectsRoot;
        InitializeComponent();
        LoadProjects();
    }

    private void LoadProjects()
    {
        if (!Directory.Exists(_projectsRoot)) return;

        var dirs = Directory.GetDirectories(_projectsRoot);
        var items = new List<ProjectListItem>();

        foreach (var dir in dirs)
        {
            var metaPath = Path.Combine(dir, "project.json");
            if (!File.Exists(metaPath)) continue;

            try
            {
                var json = File.ReadAllText(metaPath);
                var meta = JsonSerializer.Deserialize<ProjectMeta>(json);
                if (meta == null) continue;

                items.Add(new ProjectListItem
                {
                    Title = meta.Title,
                    Phase = PhaseLabel(meta.Phase),
                    Path = dir
                });
            }
            catch { /* skip corrupted */ }
        }

        ProjectList.ItemsSource = items;
    }

    private static string PhaseLabel(ProjectPhase phase) => phase switch
    {
        ProjectPhase.TopicPicked => "刚创建",
        ProjectPhase.SynopsisDone => "梗概已完成",
        ProjectPhase.OutlineDone => "大纲已完成",
        ProjectPhase.ChapterActive => "写作中",
        _ => "未知"
    };

    private void BrowseFolder_Click(object sender, RoutedEventArgs e)
    {
        var dir = Microsoft.VisualBasic.Interaction.InputBox(
            "输入项目目录路径:", "浏览文件夹", _projectsRoot, -1, -1);
        if (!string.IsNullOrWhiteSpace(dir) && Directory.Exists(dir))
        {
            SelectedPath = dir;
            DialogResult = true;
            Close();
        }
    }

    private void Open_Click(object sender, RoutedEventArgs e)
    {
        if (ProjectList.SelectedItem is ProjectListItem item)
        {
            SelectedPath = item.Path;
            DialogResult = true;
            Close();
        }
        else
        {
            ErrorText.Text = "请选择一个项目";
        }
    }

    private void ProjectList_DoubleClick(object sender, System.Windows.Input.MouseButtonEventArgs e)
    {
        Open_Click(sender, e);
    }

    private void Cancel_Click(object sender, RoutedEventArgs e)
    {
        DialogResult = false;
        Close();
    }

    public record ProjectListItem
    {
        public string Title { get; init; } = "";
        public string Phase { get; init; } = "";
        public string Path { get; init; } = "";
    }
}
