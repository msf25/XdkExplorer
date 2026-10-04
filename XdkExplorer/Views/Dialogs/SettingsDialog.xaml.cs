using System.Windows;
using XdkExplorer.ViewModels;

namespace XdkExplorer.Views;

public partial class SettingsDialog : Window
{
    private readonly SettingsViewModel _viewModel;

    public SettingsDialog(SettingsViewModel viewModel)
    {
        InitializeComponent();

        _viewModel = viewModel;
        DataContext = viewModel;
    }

    private void OnSave(object sender, RoutedEventArgs e)
    {
        _viewModel.Save();
        DialogResult = true;
    }
}
