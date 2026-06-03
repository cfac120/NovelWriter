using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;

namespace NovelWriter.App.ViewModels;

public partial class EditorViewModel : ViewModelBase
{
    [ObservableProperty]
    private string _chapterContent = string.Empty;

    [ObservableProperty]
    private string _chapterTitle = "未命名章节";

    [ObservableProperty]
    private int _wordCount;

    [ObservableProperty]
    private int _cursorPosition;

    [ObservableProperty]
    private bool _isModified;

    partial void OnChapterContentChanged(string value)
    {
        WordCount = value.Length;
        if (!IsModified) IsModified = true;
    }

    [RelayCommand]
    private void Save()
    {
        IsModified = false;
    }
}
