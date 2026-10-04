using System.Windows;
using XdkExplorer.ViewModels;

namespace XdkExplorer.Views;

public partial class NetworkScanDialog : Window
{
    private readonly NetworkScanViewModel _viewModel;

    public NetworkScanDialog(NetworkScanViewModel viewModel)
    {
        InitializeComponent();

        _viewModel = viewModel;
        DataContext = viewModel;

        Loaded += (_, _) => _viewModel.SearchCommand.Execute(null);
    }

    private void OnAddByIp(object sender, RoutedEventArgs e)
    {
        _viewModel.RequestAddByIp();
        Close();
    }

    private void OnClose(object sender, RoutedEventArgs e)
    {
        Close();
    }
}
