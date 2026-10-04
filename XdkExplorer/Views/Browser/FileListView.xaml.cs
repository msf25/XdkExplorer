using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using XdkExplorer.ViewModels;

namespace XdkExplorer.Views;

public partial class FileListView : UserControl
{
    /// <summary>Height of whatever floats over the bottom of the list, so the last rows stay reachable.</summary>
    public static readonly DependencyProperty BottomInsetProperty =
        DependencyProperty.Register(nameof(BottomInset), typeof(double), typeof(FileListView),
            new PropertyMetadata(0.0, (d, _) => ((FileListView)d).UpdateListMargin()));

    public FileListView()
    {
        InitializeComponent();
    }

    public double BottomInset
    {
        get => (double)GetValue(BottomInsetProperty);
        set => SetValue(BottomInsetProperty, value);
    }

    private FileBrowserViewModel? ViewModel => DataContext as FileBrowserViewModel;

    private void UpdateListMargin()
    {
        FileList.Margin = new Thickness(6, 0, 6, 4 + BottomInset);
    }

    // ListBox.SelectedItems cannot be bound, so the selection is pushed to the view model here
    private void OnSelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (ViewModel != null)
        {
            ViewModel.SelectedItems = FileList.SelectedItems.Cast<FileItemViewModel>().ToList();
        }
    }

    private void OnDoubleClick(object sender, MouseButtonEventArgs e)
    {
        // Ignore double clicks on the scrollbar or empty space
        if (e.OriginalSource is not DependencyObject source || ItemsControl.ContainerFromElement(FileList, source) is not ListBoxItem item)
        {
            return;
        }

        ViewModel?.Actions.OpenCommand.Execute(item.DataContext);
    }

    private void OnDragOver(object sender, DragEventArgs e)
    {
        bool canDrop = e.Data.GetDataPresent(DataFormats.FileDrop) && ViewModel?.CurrentPath != null;

        e.Effects = canDrop ? DragDropEffects.Copy : DragDropEffects.None;
        e.Handled = true;
    }

    private void OnDrop(object sender, DragEventArgs e)
    {
        if (e.Data.GetData(DataFormats.FileDrop) is string[] paths)
        {
            ViewModel?.Actions.UploadPaths(paths);
        }
    }
}
