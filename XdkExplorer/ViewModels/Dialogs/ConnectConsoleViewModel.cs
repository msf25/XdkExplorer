using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using XdkExplorer.Services;
using XdkExplorer.Xbdm;

namespace XdkExplorer.ViewModels;

/// <summary>
/// Name or IP input with a connection test, used by the Add Xbox dialog and the setup wizard.
/// A console can only be added after a successful test, like in the XDK shell extension.
/// </summary>
public sealed partial class ConnectConsoleViewModel : ObservableObject
{
    private readonly XbdmWorker _worker;
    private readonly Func<string, string?> _findDuplicate;

    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(TestCommand))]
    public partial string Input { get; set; } = "";

    [ObservableProperty]
    public partial bool SetAsDefault { get; set; }

    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(TestCommand))]
    public partial bool IsTesting { get; private set; }

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(IsConnected), nameof(HasFailed), nameof(ResultText))]
    public partial ConsoleConnectionResult? Result { get; set; }

    /// <param name="findDuplicate">Returns the name of a known console matching the given name or IP, or null.</param>
    public ConnectConsoleViewModel(XbdmWorker worker, Func<string, string?> findDuplicate)
    {
        _worker = worker;
        _findDuplicate = findDuplicate;
    }

    public bool IsConnected => Result?.IsSuccess == true;

    public bool HasFailed => Result is { IsSuccess: false };

    public string ResultText => Result?.Message ?? "";

    /// <summary>Hint above the input, e.g. that the XDK already has a default console.</summary>
    public string? Hint { get; set; }

    public SavedConsole CreateSavedConsole()
    {
        if (Result is not { IsSuccess: true } result)
        {
            throw new InvalidOperationException("Only a tested console can be added.");
        }

        return new SavedConsole { Target = result.Target, Name = result.Name, Address = result.Address };
    }

    // Any edit invalidates the last test, otherwise a different console could be added unchecked
    partial void OnInputChanged(string value)
    {
        Result = null;
    }

    [RelayCommand(CanExecute = nameof(CanTest))]
    private async Task TestAsync()
    {
        var target = Input.Trim();

        if (_findDuplicate(target) is { } known)
        {
            Result = ConsoleConnectionResult.Failure(target, $"{known} is already in the list.");

            return;
        }

        IsTesting = true;

        try
        {
            var result = await ConsoleConnectionTest.RunAsync(_worker, target);

            // The console may answer to an IP although it is listed by name, or the other way round
            if (result.IsSuccess && (_findDuplicate(result.Name!) ?? _findDuplicate(result.Address!)) is { } duplicate)
            {
                result = ConsoleConnectionResult.Failure(target, $"{target} is {duplicate}, which is already in the list.");
            }

            // Ignore the result if the input changed while the test was running
            if (string.Equals(Input.Trim(), target, StringComparison.OrdinalIgnoreCase))
            {
                Result = result;
            }
        }
        finally
        {
            IsTesting = false;
        }
    }

    private bool CanTest() => !IsTesting && Input.Trim().Length > 0;
}
