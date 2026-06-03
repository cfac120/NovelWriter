using System.Collections.ObjectModel;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using NovelWriter.Core.Dtos;

namespace NovelWriter.App.Views;

public partial class ConfirmationDialog : Window
{
    public ObservableCollection<ConfirmationItemViewModel> Items { get; } = [];
    public List<ConfirmationDecision> Decisions { get; } = [];

    public ConfirmationDialog(IReadOnlyList<ConfirmationItem> items)
    {
        InitializeComponent();
        DataContext = this;

        foreach (var item in items)
        {
            Items.Add(new ConfirmationItemViewModel(item));
        }

        ConfirmationList.ItemsSource = Items;
    }

    private void ApproveItem_Click(object sender, RoutedEventArgs e)
    {
        if (sender is Button btn && btn.Tag is ConfirmationItemViewModel item)
        {
            item.IsApproved = true;
            item.IsProcessed = true;
            item.ItemBackground = new SolidColorBrush(Color.FromRgb(0x1A, 0x2E, 0x1A));
        }
    }

    private void RejectItem_Click(object sender, RoutedEventArgs e)
    {
        if (sender is Button btn && btn.Tag is ConfirmationItemViewModel item)
        {
            item.IsApproved = false;
            item.IsProcessed = true;
            item.ItemBackground = new SolidColorBrush(Color.FromRgb(0x3C, 0x1A, 0x1A));
        }
    }

    private void ApproveAll_Click(object sender, RoutedEventArgs e)
    {
        foreach (var item in Items)
        {
            item.IsApproved = true;
            item.IsProcessed = true;
            item.ItemBackground = new SolidColorBrush(Color.FromRgb(0x1A, 0x2E, 0x1A));
        }
    }

    private void RejectAll_Click(object sender, RoutedEventArgs e)
    {
        foreach (var item in Items)
        {
            item.IsApproved = false;
            item.IsProcessed = true;
            item.ItemBackground = new SolidColorBrush(Color.FromRgb(0x3C, 0x1A, 0x1A));
        }
    }

    private void Done_Click(object sender, RoutedEventArgs e)
    {
        foreach (var item in Items)
        {
            Decisions.Add(new ConfirmationDecision
            {
                ItemId = item.Id,
                Approved = item.IsApproved,
                Note = item.Note ?? ""
            });
        }

        DialogResult = true;
        Close();
    }
}

public class ConfirmationItemViewModel
{
    public string Id { get; }
    public string TypeLabel { get; }
    public string Summary { get; }
    public Brush TypeColor { get; }
    public Brush ItemBackground { get; set; }
    public bool IsApproved { get; set; }
    public bool IsProcessed { get; set; }
    public string? Note { get; set; }

    public ConfirmationItemViewModel(ConfirmationItem item)
    {
        Id = item.GetHashCode().ToString();
        Summary = item.Summary;
        (TypeLabel, TypeColor) = item.Type switch
        {
            "NewForeshadowing" => ("新伏笔", new SolidColorBrush(Color.FromRgb(0xF9, 0xE2, 0xAF))),
            "LowConfidenceResolution" => ("伏笔回收(低置信度)", new SolidColorBrush(Color.FromRgb(0xA6, 0xE3, 0xA1))),
            "L3ChangeProposal" => ("L3人物变更", new SolidColorBrush(Color.FromRgb(0x89, 0xB4, 0xFA))),
            "L3WorldSettingChange" => ("L3设定变更", new SolidColorBrush(Color.FromRgb(0xC9, 0xA0, 0xDC))),
            "ArcMilestoneAddition" => ("弧线里程碑", new SolidColorBrush(Color.FromRgb(0x6C, 0x70, 0x86))),
            _ => (item.Type, new SolidColorBrush(Color.FromRgb(0xA6, 0xAD, 0xC8)))
        };
        ItemBackground = new SolidColorBrush(Color.FromRgb(0x31, 0x32, 0x44));
    }
}
