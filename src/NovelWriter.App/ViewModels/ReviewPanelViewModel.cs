using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;

namespace NovelWriter.App.ViewModels;

public partial class ReviewPanelViewModel : ViewModelBase
{
    [ObservableProperty]
    private double _overallScore;

    [ObservableProperty]
    private string _scoreColor = "#A6E3A1";

    [ObservableProperty]
    private ObservableCollection<PersonaReviewItem> _personaReviews = [];

    [ObservableProperty]
    private ObservableCollection<FlaggedIssueItem> _flaggedIssues = [];

    [ObservableProperty]
    private bool _isPassing;

    public record PersonaReviewItem
    {
        public string PersonaName { get; init; } = "";
        public double Score { get; init; }
        public string Status { get; init; } = "";
        public string Strengths { get; init; } = "";
        public string Weaknesses { get; init; } = "";
        public string Suggestions { get; init; } = "";
    }

    public record FlaggedIssueItem
    {
        public string Type { get; init; } = "";
        public string Detail { get; init; } = "";
    }
}
