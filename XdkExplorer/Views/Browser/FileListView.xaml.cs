using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Threading;
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

        DataContextChanged += OnDataContextChanged;
    }

    public double BottomInset
    {
        get => (double)GetValue(BottomInsetProperty);
        set => SetValue(BottomInsetProperty, value);
    }

    private FileBrowserViewModel? ViewModel => DataContext as FileBrowserViewModel;

    private void OnDataContextChanged(object sender, DependencyPropertyChangedEventArgs e)
    {
        if (e.OldValue is FileBrowserViewModel oldViewModel)
        {
            oldViewModel.ListingShown -= OnListingShown;
        }

        if (e.NewValue is FileBrowserViewModel newViewModel)
        {
            newViewModel.ListingShown += OnListingShown;
        }
    }

    private void OnListingShown(object? sender, FileItemViewModel? cameFrom)
    {
        if (cameFrom != null)
        {
            FileList.SelectAndFocus(cameFrom);
        }
        else
        {
            Dispatcher.BeginInvoke(DispatcherPriority.Loaded, FocusIfLost);
        }
    }

    /// <summary>
    /// Takes keyboard focus when nothing usable has it, e.g. after opening a folder removed the focused row.
    /// Focus somewhere else, like the sidebar or the filter box, stays where it is. An empty folder has no list,
    /// the background takes focus then so Shift+F10 still opens its menu.
    /// </summary>
    private void FocusIfLost()
    {
        var focused = Keyboard.FocusedElement as UIElement;

        if (focused == null || focused is Window || !focused.IsVisible || IsKeyboardFocusWithin)
        {
            if (FileList.IsVisible)
            {
                FileList.Focus();
            }
            else
            {
                Root.Focus();
            }
        }
    }

    private void OnListKeyDown(object sender, KeyEventArgs e)
    {
        if (e.Key != Key.Enter || Keyboard.Modifiers != ModifierKeys.None || FileList.SelectedItems.Count != 1)
        {
            return;
        }

        ViewModel?.Actions.OpenCommand.Execute(FileList.SelectedItem);
        e.Handled = true;
    }

    // Right-click on free space below the rows shows the background menu, like in Explorer
    private void OnListMenuOpening(object sender, ContextMenuEventArgs e)
    {
        if (e.OriginalSource is not DependencyObject source || IsInside<ScrollBar>(source))
        {
            return;
        }

        if (ItemsControl.ContainerFromElement(FileList, source) is ListBoxItem)
        {
            return;
        }

        e.Handled = true;
        FileList.UnselectAll();

        var menu = Root.ContextMenu;
        menu.DataContext = DataContext;
        menu.PlacementTarget = FileList;
        menu.Placement = e.CursorLeft < 0 ? PlacementMode.Center : PlacementMode.MousePoint;
        menu.IsOpen = true;
    }

    // Loading, offline and error states have no folder to act on
    private void OnBackgroundMenuOpening(object sender, ContextMenuEventArgs e)
    {
        if (ViewModel?.State is not (BrowserState.Items or BrowserState.Empty))
        {
            e.Handled = true;
        }
    }

    private static bool IsInside<T>(DependencyObject element) where T : DependencyObject
    {
        for (var current = element; current != null; current = VisualTreeHelper.GetParent(current))
        {
            if (current is T)
            {
                return true;
            }
        }

        return false;
    }

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
