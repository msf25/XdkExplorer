using System.Windows;
using XdkExplorer.ViewModels;

namespace XdkExplorer.Views;

public partial class SetupWizard : Window
{
    public SetupWizard(SetupWizardViewModel viewModel)
    {
        InitializeComponent();

        DataContext = viewModel;

        viewModel.CloseRequested += (_, _) => Close();
    }
}
