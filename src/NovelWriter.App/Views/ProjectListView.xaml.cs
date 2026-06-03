using System.Windows.Controls;
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
                vm.LoadProjectsCommand.Execute(null);
        };
    }
}
