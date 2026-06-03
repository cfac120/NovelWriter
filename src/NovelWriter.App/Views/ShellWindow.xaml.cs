using System.Windows;
using NovelWriter.App.ViewModels;

namespace NovelWriter.App.Views;

public partial class ShellWindow : Window
{
    public ShellWindow()
    {
        InitializeComponent();
        Loaded += OnLoaded;
    }

    private void OnLoaded(object sender, RoutedEventArgs e)
    {
        // 更新 API 状态
        var key = System.Environment.GetEnvironmentVariable("DEEPSEEK_API_KEY");
        ApiStatus.Text = string.IsNullOrEmpty(key) ? "未配置" : "已配置";
        ApiStatus.Foreground = string.IsNullOrEmpty(key)
            ? new System.Windows.Media.SolidColorBrush(System.Windows.Media.Color.FromRgb(0xF3, 0x8B, 0xA8))
            : new System.Windows.Media.SolidColorBrush(System.Windows.Media.Color.FromRgb(0xA6, 0xE3, 0xA1));
    }

    private void BackToProjects(object sender, RoutedEventArgs e)
    {
        if (DataContext is ShellViewModel vm)
            vm.NavigateToCommand.Execute("projects");
    }

    private void ApiStatus_Click(object sender, System.Windows.Input.MouseButtonEventArgs e)
    {
        var dlg = new SettingsDialog { Owner = this };
        dlg.ShowDialog();
        OnLoaded(this, e);
    }
}
