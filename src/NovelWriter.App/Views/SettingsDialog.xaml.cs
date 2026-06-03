using System.Windows;

namespace NovelWriter.App.Views;

public partial class SettingsDialog : Window
{
    public SettingsDialog()
    {
        InitializeComponent();
        ApiKeyBox.Text = System.Environment.GetEnvironmentVariable("DEEPSEEK_API_KEY") ?? "";
    }

    private void Save_Click(object sender, RoutedEventArgs e)
    {
        var key = ApiKeyBox.Text.Trim();
        System.Environment.SetEnvironmentVariable("DEEPSEEK_API_KEY", key,
            System.EnvironmentVariableTarget.Process);
        DialogResult = true;
        Close();
    }

    private void Cancel_Click(object sender, RoutedEventArgs e)
    {
        DialogResult = false;
        Close();
    }
}
