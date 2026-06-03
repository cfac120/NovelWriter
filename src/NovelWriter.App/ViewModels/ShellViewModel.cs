using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using NovelWriter.Core.Entities;

namespace NovelWriter.App.ViewModels;

public partial class ShellViewModel : ObservableObject
{
    [ObservableProperty]
    private ViewModelBase _activeView;

    [ObservableProperty]
    private string _statusText = "就绪";

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
        ActiveView = view switch
        {
            "editor" => new EditorViewModel(),
            _ => new ProjectListViewModel()
        };
    }
}
