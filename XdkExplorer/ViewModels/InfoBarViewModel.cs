using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;

namespace XdkExplorer.ViewModels;

/// <summary>The message bar above the file list. Shared by all parts of the window to report results and errors.</summary>
public sealed partial class InfoBarViewModel : ObservableObject
{
    [ObservableProperty]
    public partial string? Message { get; private set; }

    public void Show(string message)
    {
        Message = message;
    }

    [RelayCommand]
    private void Close()
    {
        Message = null;
    }
}
