using System.Collections.ObjectModel;
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

    [ObservableProperty]
    private bool _isGenerating;

    [ObservableProperty]
    private string _statusText = "就绪";

    [ObservableProperty]
    private ObservableCollection<ChapterItem> _chapters = [];

    partial void OnChapterContentChanged(string value)
    {
        WordCount = value.Length;
        if (!IsModified) IsModified = true;
    }

    [RelayCommand]
    private void Save()
    {
        IsModified = false;
        StatusText = "已保存";
    }

    [RelayCommand]
    private void StopGeneration()
    {
        IsGenerating = false;
        StatusText = "生成已取消";
    }

    [RelayCommand]
    private async Task GenerateChapter()
    {
        IsGenerating = true;
        StatusText = "正在生成...";
        try
        {
            await Task.Delay(100);
        }
        finally
        {
            IsGenerating = false;
            StatusText = "生成完成";
        }
    }

    public record ChapterItem(int Number, string Title, int Words);
}
