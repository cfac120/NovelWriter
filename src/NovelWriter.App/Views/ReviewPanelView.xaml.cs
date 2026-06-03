using System.Windows;
using System.Windows.Controls;

namespace NovelWriter.App.Views;

public partial class ReviewPanelView : UserControl
{
    public ReviewPanelView()
    {
        InitializeComponent();
    }

    private void AcceptBtn_Click(object sender, RoutedEventArgs e)
    {
        MessageBox.Show("已接受当前版本并定稿。", "NovelWriter",
            MessageBoxButton.OK, MessageBoxImage.Information);
    }

    private void ReviseBtn_Click(object sender, RoutedEventArgs e)
    {
        MessageBox.Show("将触发润色重写流程。", "NovelWriter",
            MessageBoxButton.OK, MessageBoxImage.Information);
    }

    private void SkipBtn_Click(object sender, RoutedEventArgs e)
    {
        var result = MessageBox.Show("确定要跳过评审吗？未评审的章节可能存在质量问题。",
            "NovelWriter", MessageBoxButton.YesNo, MessageBoxImage.Warning);
    }
}
