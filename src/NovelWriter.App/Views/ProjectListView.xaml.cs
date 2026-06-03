using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using NovelWriter.App.ViewModels;

namespace NovelWriter.App.Views;

public partial class ProjectListView : UserControl
{
    public ProjectListView()
    {
        InitializeComponent();
        Loaded += async (s, e) =>
        {
            if (DataContext is ProjectListViewModel vm)
                await vm.LoadProjectsCommand.ExecuteAsync(null);
        };
    }

    private void ProjectClicked(object sender, MouseButtonEventArgs e)
    {
        if (sender is FrameworkElement el && el.Tag is string id
            && DataContext is ProjectListViewModel vm)
        {
            vm.OpenProjectCommand.Execute(id);
        }
    }
}
