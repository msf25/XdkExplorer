using System.Windows;
using Microsoft.Win32;
using XdkExplorer.ViewModels;

namespace XdkExplorer.Views;

public sealed class DialogService : IDialogService
{
    private static Window? Owner => Application.Current.MainWindow;

    public string? AskText(string title, string label, string initialText, Func<string, string?> validate)
    {
        var dialog = new TextInputDialog(title, label, initialText, validate) { Owner = Owner };

        return dialog.ShowDialog() == true ? dialog.Value : null;
    }

    public bool Confirm(string title, string message, string confirmText)
    {
        var dialog = new ConfirmDialog(title, message, confirmText) { Owner = Owner };

        return dialog.ShowDialog() == true;
    }

    public string[]? PickFiles(string title)
    {
        var dialog = new OpenFileDialog
        {
            Title = title,
            Multiselect = true
        };

        return (Owner == null ? dialog.ShowDialog() : dialog.ShowDialog(Owner)) == true ? dialog.FileNames : null;
    }

    public string? PickFolder(string title)
    {
        var dialog = new OpenFolderDialog { Title = title };

        return (Owner == null ? dialog.ShowDialog() : dialog.ShowDialog(Owner)) == true ? dialog.FolderName : null;
    }

    public string? PickSaveFile(string title, string initialFolder, string fileName, string filter)
    {
        var dialog = new SaveFileDialog
        {
            Title = title,
            InitialDirectory = initialFolder,
            FileName = fileName,
            Filter = filter,
            AddExtension = true,
            OverwritePrompt = true
        };

        return (Owner == null ? dialog.ShowDialog() : dialog.ShowDialog(Owner)) == true ? dialog.FileName : null;
    }

    public bool ShowAddConsole(ConnectConsoleViewModel form)
    {
        var dialog = new AddConsoleDialog(form) { Owner = Owner };

        return dialog.ShowDialog() == true;
    }

    public bool ShowSettings(SettingsViewModel settings)
    {
        var dialog = new SettingsDialog(settings) { Owner = Owner };

        return dialog.ShowDialog() == true;
    }

    public void ShowNetworkScan(NetworkScanViewModel scan)
    {
        new NetworkScanDialog(scan) { Owner = Owner }.ShowDialog();
    }

    public (ConflictChoice Choice, bool ApplyToAll) Resolve(TransferKind kind, string targetPath, string consoleName, int remainingFiles)
    {
        var dialog = new ConflictDialog(kind, targetPath, consoleName, remainingFiles) { Owner = Owner };

        if (dialog.ShowDialog() != true)
        {
            return (ConflictChoice.Cancel, false);
        }

        return (dialog.Choice, dialog.ApplyToAll);
    }
}
