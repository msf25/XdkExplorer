using System.Windows;
using XdkExplorer.ViewModels;

namespace XdkExplorer.Views;

public partial class ConflictDialog : Window
{
    public ConflictDialog(TransferKind kind, string targetPath, string consoleName, int remainingFiles)
    {
        InitializeComponent();

        MessageText.Text = kind == TransferKind.Upload
            ? $"This file already exists on {consoleName}:"
            : "This file already exists on this PC:";

        FileText.Text = targetPath;

        ApplyToAllBox.Content = $"Do this for all further conflicts ({remainingFiles} files left)";
        ApplyToAllBox.Visibility = remainingFiles > 0 ? Visibility.Visible : Visibility.Collapsed;
    }

    public ConflictChoice Choice { get; private set; } = ConflictChoice.Cancel;

    public bool ApplyToAll => ApplyToAllBox.IsChecked == true;

    private void OnReplace(object sender, RoutedEventArgs e)
    {
        Choice = ConflictChoice.Replace;
        DialogResult = true;
    }

    private void OnSkip(object sender, RoutedEventArgs e)
    {
        Choice = ConflictChoice.Skip;
        DialogResult = true;
    }
}
