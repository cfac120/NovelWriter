using System.Windows;
using System.Windows.Controls;
using NovelWriter.App.ViewModels;

namespace NovelWriter.App.Views;

public partial class EditorView : UserControl
{
    public EditorView()
    {
        InitializeComponent();
    }

    private void EditorTextBox_SelectionChanged(object sender, RoutedEventArgs e)
    {
        if (DataContext is EditorViewModel vm)
        {
            vm.CursorPosition = EditorTextBox.CaretIndex;
        }
    }
}
