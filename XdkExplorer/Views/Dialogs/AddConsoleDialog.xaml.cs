using System.Windows;
using XdkExplorer.ViewModels;

namespace XdkExplorer.Views;

public partial class AddConsoleDialog : Window
{
    public AddConsoleDialog(ConnectConsoleViewModel viewModel)
    {
        InitializeComponent();

        DataContext = viewModel;
    }

    private void OnAdd(object sender, RoutedEventArgs e)
    {
        DialogResult = true;
    }
}
