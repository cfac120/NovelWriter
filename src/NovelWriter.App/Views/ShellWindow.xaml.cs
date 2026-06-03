using System.Windows;
using NovelWriter.App.ViewModels;

namespace NovelWriter.App.Views;

public partial class ShellWindow : Window
{
    public ShellWindow() => InitializeComponent();

    private void BackToProjects(object sender, RoutedEventArgs e)
    {
        if (DataContext is ShellViewModel vm)
            vm.NavigateToCommand.Execute("projects");
    }
}
