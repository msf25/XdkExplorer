namespace XdkExplorer.ViewModels;

public interface IDialogService : IConflictResolver
{
    /// <param name="validate">Returns an error message for invalid input, or null.</param>
    string? AskText(string title, string label, string initialText, Func<string, string?> validate);

    bool Confirm(string title, string message, string confirmText);

    string[]? PickFiles(string title);

    string? PickFolder(string title);

    /// <param name="filter">File dialog filter such as "Bitmap (*.bmp)|*.bmp".</param>
    string? PickSaveFile(string title, string initialFolder, string fileName, string filter);

    /// <summary>Returns true if the user added the tested console.</summary>
    bool ShowAddConsole(ConnectConsoleViewModel form);

    /// <summary>Returns true if the settings were saved.</summary>
    bool ShowSettings(SettingsViewModel settings);

    void ShowNetworkScan(NetworkScanViewModel scan);
}
