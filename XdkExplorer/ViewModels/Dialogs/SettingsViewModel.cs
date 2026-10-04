using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using XdkExplorer.Services;

namespace XdkExplorer.ViewModels;

public sealed class BlockSizeOption
{
    public BlockSizeOption(int bytes, string label)
    {
        Bytes = bytes;
        Label = label;
    }

    public int Bytes { get; }

    public string Label { get; }
}

/// <summary>Settings dialog. Changes are only written to the settings file on Save.</summary>
public sealed partial class SettingsViewModel : ObservableObject
{
    private readonly AppSettings _settings;
    private readonly IDialogService _dialogs;
    private readonly string? _loadedDllPath;
    private string? _manualDllPath;

    [ObservableProperty]
    public partial BlockSizeOption SelectedBlockSize { get; set; }

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(IsRestartRequired))]
    public partial string? XdkPath { get; private set; }

    [ObservableProperty]
    public partial string XdkStatus { get; private set; } = "";

    [ObservableProperty]
    public partial string? XdkError { get; private set; }

    /// <param name="loadedDllPath">The xboxdbg.dll this process uses, to tell whether a change needs a restart.</param>
    public SettingsViewModel(AppSettings settings, IDialogService dialogs, string? loadedDllPath)
    {
        _settings = settings;
        _dialogs = dialogs;
        _loadedDllPath = loadedDllPath;
        _manualDllPath = settings.XdkDllPath;

        SelectedBlockSize = BlockSizes.FirstOrDefault(b => b.Bytes == settings.ChunkSize) ?? BlockSizes[1];

        ShowXdk(_manualDllPath != null ? XdkLocator.FromDll(_manualDllPath) : XdkLocator.FindInstalled(), _manualDllPath != null);
    }

    public IReadOnlyList<BlockSizeOption> BlockSizes { get; } =
    [
        new(64 * 1024, "64 KB"),
        new(256 * 1024, "256 KB (default)"),
        new(1024 * 1024, "1 MB")
    ];

    public string AppVersionText => $"XDK Explorer {AppInfo.Version}";

    public string RuntimeText => AppInfo.Runtime;

    public bool IsRestartRequired => XdkPath != null && !string.Equals(XdkPath, _loadedDllPath, StringComparison.OrdinalIgnoreCase);

    public void Save()
    {
        _settings.ChunkSize = SelectedBlockSize.Bytes;
        _settings.XdkDllPath = _manualDllPath;
        _settings.Save();
    }

    [RelayCommand]
    private void ChangeXdk()
    {
        var folder = _dialogs.PickFolder("Folder of the Xbox Development Kit");

        if (folder == null)
        {
            return;
        }

        var installation = XdkLocator.FindInFolder(folder);

        if (installation == null)
        {
            XdkError = "No xboxdbg.dll was found in this folder or its subfolders.";

            return;
        }

        _manualDllPath = installation.DllPath;

        ShowXdk(installation, true);
    }

    [RelayCommand]
    private void SearchAgain()
    {
        _manualDllPath = null;

        ShowXdk(XdkLocator.FindInstalled(), false);
    }

    private void ShowXdk(XdkInstallation? installation, bool isManual)
    {
        XdkError = null;

        if (installation == null)
        {
            XdkPath = null;
            XdkStatus = isManual ? "The chosen xboxdbg.dll no longer exists." : "No installed XDK found.";

            return;
        }

        XdkPath = installation.DllPath;

        var source = isManual ? "Chosen manually" : "Detected automatically";

        XdkStatus = installation.Version == null ? source : $"{source} · Version {installation.Version}";
    }
}
