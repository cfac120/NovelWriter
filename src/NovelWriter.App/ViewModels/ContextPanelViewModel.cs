using CommunityToolkit.Mvvm.ComponentModel;

namespace NovelWriter.App.ViewModels;

public partial class ContextPanelViewModel : ViewModelBase
{
    [ObservableProperty]
    private string _l1Summaries = string.Empty;

    [ObservableProperty]
    private string _l2Status = string.Empty;

    [ObservableProperty]
    private string _l3Entities = string.Empty;

    [ObservableProperty]
    private int _totalTokens;

    [ObservableProperty]
    private int _maxTokens = 160_000;

    [ObservableProperty]
    private double _tokenUsagePercent;
}
