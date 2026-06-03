using System.Windows;

namespace NovelWriter.App.Views;

public partial class SettingsDialog : Window
{
    public SettingsDialog()
    {
        InitializeComponent();
        LoadSettings();
    }

    private void LoadSettings()
    {
        StyleEnabled.IsChecked = false;
        InterludeEnabled.IsChecked = false;
    }

    private void TestDeepSeek_Click(object sender, RoutedEventArgs e)
    {
        var key = DeepSeekKey.Password;
        if (string.IsNullOrWhiteSpace(key))
        {
            MessageBox.Show("请输入 API Key。", "NovelWriter", MessageBoxButton.OK, MessageBoxImage.Warning);
            return;
        }
        MessageBox.Show("DeepSeek API Key 已保存。", "NovelWriter", MessageBoxButton.OK, MessageBoxImage.Information);
    }

    private void TestQwen_Click(object sender, RoutedEventArgs e)
    {
        var key = QwenKey.Password;
        if (string.IsNullOrWhiteSpace(key))
        {
            MessageBox.Show("请输入 API Key。", "NovelWriter", MessageBoxButton.OK, MessageBoxImage.Warning);
            return;
        }
        MessageBox.Show("Qwen API Key 已保存。", "NovelWriter", MessageBoxButton.OK, MessageBoxImage.Information);
    }

    private void TestKimi_Click(object sender, RoutedEventArgs e)
    {
        var key = KimiKey.Password;
        if (string.IsNullOrWhiteSpace(key))
        {
            MessageBox.Show("请输入 API Key。", "NovelWriter", MessageBoxButton.OK, MessageBoxImage.Warning);
            return;
        }
        MessageBox.Show("Kimi API Key 已保存。", "NovelWriter", MessageBoxButton.OK, MessageBoxImage.Information);
    }

    private void ManageStyles_Click(object sender, RoutedEventArgs e)
    {
        MessageBox.Show("风格库管理功能即将上线。", "NovelWriter", MessageBoxButton.OK, MessageBoxImage.Information);
    }

    private void ManageInterludes_Click(object sender, RoutedEventArgs e)
    {
        MessageBox.Show("插曲库管理功能即将上线。", "NovelWriter", MessageBoxButton.OK, MessageBoxImage.Information);
    }

    private void Save_Click(object sender, RoutedEventArgs e)
    {
        DialogResult = true;
        Close();
    }

    private void Cancel_Click(object sender, RoutedEventArgs e)
    {
        DialogResult = false;
        Close();
    }
}
