using System.Windows;
using System.Windows.Input;
using XdkExplorer.ViewModels;

namespace XdkExplorer.Views;

public partial class MainWindow : Window
{
    private readonly MainViewModel _viewModel;

    public MainWindow(MainViewModel viewModel)
    {
        InitializeComponent();

        _viewModel = viewModel;
        DataContext = viewModel;

        Loaded += async (_, _) => await viewModel.InitializeAsync();

        // Pick up consoles added or removed in the Xbox Neighborhood while this window was in the background
        Activated += (_, _) => viewModel.Consoles.Sync();
    }

    // MouseBinding has no gesture for the side buttons, so back and forward are mapped here.
    // Preview, so a focused list or text box cannot swallow the click.
    protected override void OnPreviewMouseDown(MouseButtonEventArgs e)
    {
        base.OnPreviewMouseDown(e);

        var command = e.ChangedButton switch
        {
            MouseButton.XButton1 => _viewModel.Browser.BackCommand,
            MouseButton.XButton2 => _viewModel.Browser.ForwardCommand,
            _ => null
        };

        if (command == null)
        {
            return;
        }

        if (command.CanExecute(null))
        {
            command.Execute(null);
        }

        e.Handled = true;
    }
}
