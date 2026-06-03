using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;

namespace NovelWriter.App.ViewModels;

public partial class ShellViewModel : ObservableObject
{
    [ObservableProperty]
    private ViewModelBase _activeView;

    [ObservableProperty]
    private ViewModelBase? _centerView;

    [ObservableProperty]
    private ViewModelBase? _rightPanelView;

    [ObservableProperty]
    private string _statusText = "就绪";

    [ObservableProperty]
    private string _pipelineStageText = string.Empty;

    [ObservableProperty]
    private string _llmStatusText = "DeepSeek V4";

    [ObservableProperty]
    private int _projectCount;

    [ObservableProperty]
    private int _chapterCount;

    [ObservableProperty]
    private int _totalWordCount;

    public ShellViewModel()
    {
        ActiveView = new ProjectListViewModel();
    }

    [RelayCommand]
    private void NavigateTo(string view)
    {
        switch (view)
        {
            case "editor":
                var editorVm = new EditorViewModel();
                ActiveView = editorVm;
                CenterView = editorVm;
                break;
            case "projects":
                ActiveView = new ProjectListViewModel();
                CenterView = null;
                RightPanelView = null;
                break;
            default:
                ActiveView = new ProjectListViewModel();
                CenterView = null;
                RightPanelView = null;
                break;
        }
    }

    public void OpenProject(string projectId, string projectTitle)
    {
        var editorVm = new EditorViewModel
        {
            ChapterTitle = projectTitle
        };
        CenterView = editorVm;
        StatusText = $"正在编辑: {projectTitle}";
        PipelineStageText = "Stage04: 写前准备";
    }
}
