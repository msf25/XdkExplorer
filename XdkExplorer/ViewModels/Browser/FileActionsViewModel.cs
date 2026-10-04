using System.ComponentModel;
using System.IO;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using XdkExplorer.Services;
using XdkExplorer.Xbdm;

namespace XdkExplorer.ViewModels;

/// <summary>Everything that changes files on the console: toolbar, context menu and double click.</summary>
public sealed partial class FileActionsViewModel : ObservableObject
{
    private readonly XbdmWorker _worker;
    private readonly AppSettings _settings;
    private readonly IDialogService _dialogs;
    private readonly InfoBarViewModel _infoBar;
    private readonly TransfersViewModel _transfers;
    private readonly FileBrowserViewModel _browser;

    public FileActionsViewModel(XbdmWorker worker, AppSettings settings, IDialogService dialogs, InfoBarViewModel infoBar,
        TransfersViewModel transfers, FileBrowserViewModel browser)
    {
        _worker = worker;
        _settings = settings;
        _dialogs = dialogs;
        _infoBar = infoBar;
        _transfers = transfers;
        _browser = browser;

        _browser.PropertyChanged += OnBrowserChanged;
    }

    public bool CanLaunch => SingleSelection?.IsXbe == true;

    private FileItemViewModel? SingleSelection => _browser.SelectedItems.Count == 1 ? _browser.SelectedItems[0] : null;

    [RelayCommand]
    private async Task OpenAsync(FileItemViewModel? item)
    {
        if (item == null)
        {
            return;
        }

        if (item.IsDirectory)
        {
            await _browser.NavigateAsync(item.FullPath, true);
        }
        else if (item.IsXbe)
        {
            await LaunchXbeAsync(item);
        }
    }

    [RelayCommand(CanExecute = nameof(HasLocation))]
    private async Task NewFolderAsync()
    {
        var name = _dialogs.AskText("New folder", "Folder name", "New folder", ValidateName);

        if (name == null)
        {
            return;
        }

        await RunFileOperationAsync(() => XbdmCommands.CreateDirectory(XboxPath.Combine(_browser.CurrentPath!, name)));
    }

    private bool HasLocation() => _browser.CurrentPath != null && _browser.Console != null;

    [RelayCommand(CanExecute = nameof(HasLocation))]
    private void Upload()
    {
        var files = _dialogs.PickFiles("Upload to " + _browser.CurrentPath);

        if (files != null)
        {
            UploadPaths(files);
        }
    }

    /// <summary>Uploads local files and folders into the current directory, e.g. from drag and drop.</summary>
    public void UploadPaths(IEnumerable<string> paths)
    {
        var console = _browser.Console;
        var targetPath = _browser.CurrentPath;

        if (console == null || targetPath == null)
        {
            return;
        }

        var sources = paths
            .Select(p => new TransferSource(p, Directory.Exists(p), 0))
            .ToList();

        if (sources.Count == 0)
        {
            return;
        }

        // The browser reloads by itself when the job reports changes in the folder it shows
        _transfers.Enqueue(new TransferJobViewModel(_worker, _dialogs, console, TransferKind.Upload, sources, targetPath, _settings.ChunkSize));
    }

    [RelayCommand(CanExecute = nameof(HasSelection))]
    private void Download()
    {
        var folder = _dialogs.PickFolder("Download to");
        var console = _browser.Console;

        if (folder == null || console == null)
        {
            return;
        }

        var sources = _browser.SelectedItems
            .Select(i => new TransferSource(i.FullPath, i.IsDirectory, i.Size))
            .ToList();

        _transfers.Enqueue(new TransferJobViewModel(_worker, _dialogs, console, TransferKind.Download, sources, folder, _settings.ChunkSize));
    }

    private bool HasSelection() => _browser.SelectedItems.Count > 0;

    [RelayCommand(CanExecute = nameof(HasSingleSelection))]
    private async Task RenameAsync()
    {
        var item = SingleSelection!;
        var name = _dialogs.AskText("Rename", "New name", item.Name, ValidateName);

        if (name == null || name == item.Name)
        {
            return;
        }

        var target = XboxPath.Combine(XboxPath.GetParent(item.FullPath)!, name);

        await RunFileOperationAsync(() => XbdmCommands.Rename(item.FullPath, target));
    }

    private bool HasSingleSelection() => _browser.SelectedItems.Count == 1;

    [RelayCommand(CanExecute = nameof(HasSelection))]
    private async Task DeleteAsync()
    {
        var items = _browser.SelectedItems.ToList();
        var what = items.Count == 1 ? $"\"{items[0].Name}\"" : $"{items.Count} items";
        var hasFolders = items.Any(i => i.IsDirectory);
        var message = $"Delete {what} on {_browser.Console?.DisplayName}?" + (hasFolders ? " Folders are deleted with all their content." : "");

        if (!_dialogs.Confirm("Delete", message, "Delete"))
        {
            return;
        }

        await RunFileOperationAsync(() =>
        {
            foreach (var item in items)
            {
                XbdmCommands.DeleteRecursive(item.FullPath, item.IsDirectory);
            }
        });
    }

    [RelayCommand(CanExecute = nameof(CanLaunch))]
    private async Task LaunchAsync()
    {
        await LaunchXbeAsync(SingleSelection!);
    }

    private async Task LaunchXbeAsync(FileItemViewModel item)
    {
        var console = _browser.Console;

        if (console == null)
        {
            return;
        }

        _infoBar.Show($"Starting {item.Name} on {console.DisplayName} …");

        try
        {
            await console.LaunchAsync(item.FullPath, true);

            _infoBar.Show($"{item.Name} started on {console.DisplayName}.");
        }
        catch (XbdmException ex)
        {
            _infoBar.Show($"Could not start {item.Name}: {ex.Message}");
        }
    }

    private async Task RunFileOperationAsync(Action operation)
    {
        var console = _browser.Console;
        var path = _browser.CurrentPath;

        if (console == null || path == null)
        {
            return;
        }

        try
        {
            await _worker.RunAsync(console.Target, operation);
        }
        catch (XbdmException ex)
        {
            _infoBar.Show(ex.Message);
        }

        await _browser.ReloadAsync();
    }

    private static string? ValidateName(string name)
    {
        if (name.Length > XboxPath.MaxNameLength)
        {
            return $"FATX allows at most {XboxPath.MaxNameLength} characters ({name.Length} entered).";
        }

        return XboxPath.IsValidName(name) ? null : "The name contains characters that are not allowed.";
    }

    private void OnBrowserChanged(object? sender, PropertyChangedEventArgs e)
    {
        switch (e.PropertyName)
        {
            case nameof(FileBrowserViewModel.CurrentPath):
            case nameof(FileBrowserViewModel.Console):
                NewFolderCommand.NotifyCanExecuteChanged();
                UploadCommand.NotifyCanExecuteChanged();
                break;

            case nameof(FileBrowserViewModel.SelectedItems):
                OnPropertyChanged(nameof(CanLaunch));
                DownloadCommand.NotifyCanExecuteChanged();
                RenameCommand.NotifyCanExecuteChanged();
                DeleteCommand.NotifyCanExecuteChanged();
                LaunchCommand.NotifyCanExecuteChanged();
                break;
        }
    }
}
