using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using XdkExplorer.Services;
using XdkExplorer.Xbdm;

namespace XdkExplorer.ViewModels;

public enum WizardStep
{
    Xdk,
    Console,
    Done
}

/// <summary>
/// First start and missing XDK. Step 1 picks xboxdbg.dll, step 2 adds a first console if the list is empty.
/// Leaving step 1 loads the DLL and starts the worker, both are handed over to the main window afterwards.
/// </summary>
public sealed partial class SetupWizardViewModel : ObservableObject
{
    private readonly AppSettings _settings;
    private readonly IDialogService _dialogs;
    private readonly bool _needsConsole;
    private bool _isManualXdk;
    private bool _isConsoleSkipped;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(IsXdkStep), nameof(IsConsoleStep), nameof(IsDoneStep), nameof(StepText), nameof(CanGoBack))]
    [NotifyCanExecuteChangedFor(nameof(NextCommand), nameof(BackCommand))]
    public partial WizardStep Step { get; private set; } = WizardStep.Xdk;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(IsXdkFound), nameof(XdkVersionText))]
    [NotifyCanExecuteChangedFor(nameof(NextCommand))]
    public partial XdkInstallation? Xdk { get; private set; }

    [ObservableProperty]
    public partial string? XdkError { get; private set; }

    [ObservableProperty]
    public partial ConnectConsoleViewModel? Connect { get; private set; }

    public SetupWizardViewModel(AppSettings settings, IDialogService dialogs)
    {
        _settings = settings;
        _dialogs = dialogs;
        _needsConsole = ConsoleRegistry.ReadConsoles().Count == 0;

        if (settings.XdkDllPath != null && XdkLocator.FromDll(settings.XdkDllPath) is { } configured)
        {
            Xdk = configured;
            _isManualXdk = true;
        }
        else
        {
            Xdk = XdkLocator.FindInstalled();
        }
    }

    public event EventHandler? CloseRequested;

    /// <summary>Created when step 1 is left. Owned by the caller once the wizard completed.</summary>
    public XbdmWorker? Worker { get; private set; }

    public bool IsCompleted { get; private set; }

    public bool IsXdkStep => Step == WizardStep.Xdk;

    public bool IsConsoleStep => Step == WizardStep.Console;

    public bool IsDoneStep => Step == WizardStep.Done;

    public bool IsXdkFound => Xdk != null;

    /// <summary>The DLL cannot be swapped once loaded, so the choice is fixed after step 1.</summary>
    public bool IsXdkLocked => Worker != null;

    public string XdkVersionText => Xdk?.Version ?? "unknown";

    public bool CanGoBack => Step != WizardStep.Xdk;

    public string StepText
    {
        get
        {
            int count = _needsConsole ? 3 : 2;
            int number = Step switch
            {
                WizardStep.Xdk => 1,
                WizardStep.Console => 2,
                _ => count
            };

            return $"Step {number} of {count}";
        }
    }

    public string ConsoleSummary
    {
        get
        {
            if (_isConsoleSkipped || Connect?.Result is not { IsSuccess: true } result)
            {
                if (_needsConsole)
                {
                    return "No console added. Add one later in the sidebar.";
                }

                var names = string.Join(", ", ConsoleRegistry.ReadConsoles());

                return $"Known consoles: {names}";
            }

            return Connect.SetAsDefault ? $"{result.Name} added · {result.Address} · default console" : $"{result.Name} added · {result.Address}";
        }
    }

    [RelayCommand]
    private void ChooseFolder()
    {
        if (IsXdkLocked)
        {
            return;
        }

        var folder = _dialogs.PickFolder("Folder of the Xbox Development Kit");

        if (folder == null)
        {
            return;
        }

        var installation = XdkLocator.FindInFolder(folder);

        if (installation == null)
        {
            XdkError = $"No xboxdbg.dll was found in {folder} or its subfolders.";

            return;
        }

        XdkError = null;
        Xdk = installation;
        _isManualXdk = true;
    }

    [RelayCommand]
    private void SearchAgain()
    {
        if (IsXdkLocked)
        {
            return;
        }

        XdkError = null;
        Xdk = XdkLocator.FindInstalled();
        _isManualXdk = false;

        if (Xdk == null)
        {
            XdkError = "No installed XDK was found. Choose the folder of the XDK manually.";
        }
    }

    [RelayCommand(CanExecute = nameof(CanGoNext))]
    private void Next()
    {
        if (Step == WizardStep.Xdk)
        {
            StartWorker();

            Step = _needsConsole ? WizardStep.Console : WizardStep.Done;
        }
        else if (Step == WizardStep.Console)
        {
            _isConsoleSkipped = false;
            Step = WizardStep.Done;
        }

        OnPropertyChanged(nameof(ConsoleSummary));
    }

    private bool CanGoNext()
    {
        return Step switch
        {
            WizardStep.Xdk => Xdk != null,
            WizardStep.Console => Connect?.IsConnected == true,
            _ => false
        };
    }

    [RelayCommand]
    private void Skip()
    {
        _isConsoleSkipped = true;
        Step = WizardStep.Done;

        OnPropertyChanged(nameof(ConsoleSummary));
    }

    [RelayCommand(CanExecute = nameof(CanGoBack))]
    private void Back()
    {
        Step = Step == WizardStep.Done && _needsConsole ? WizardStep.Console : WizardStep.Xdk;

        if (Step == WizardStep.Xdk)
        {
            OnPropertyChanged(nameof(IsXdkLocked));
        }
    }

    [RelayCommand]
    private void Finish()
    {
        _settings.XdkDllPath = _isManualXdk ? Xdk!.DllPath : null;

        if (!_isConsoleSkipped && Connect is { IsConnected: true } connect)
        {
            var saved = connect.CreateSavedConsole();
            var info = _settings.GetConsoleInfo(saved.Target);

            info.Name = saved.Name;
            info.Address = saved.Address;

            ConsoleRegistry.Add(saved.Target);

            if (connect.SetAsDefault)
            {
                ConsoleRegistry.SetDefault(saved.Target);
            }
        }

        _settings.Save();
        IsCompleted = true;

        CloseRequested?.Invoke(this, EventArgs.Empty);
    }

    private void StartWorker()
    {
        if (Worker != null)
        {
            return;
        }

        XboxDbgNative.UseDll(Xdk!.DllPath);
        Worker = new XbdmWorker();

        OnPropertyChanged(nameof(IsXdkLocked));

        if (_needsConsole)
        {
            var defaultName = ConsoleRegistry.ReadDefault();

            Connect = new ConnectConsoleViewModel(Worker, _ => null)
            {
                Input = defaultName ?? "",
                Hint = defaultName == null ? null : $"Found: {defaultName}, set as default console in the XDK."
            };

            // The Next button depends on the test result of the form
            Connect.PropertyChanged += (_, e) =>
            {
                if (e.PropertyName == nameof(ConnectConsoleViewModel.IsConnected))
                {
                    NextCommand.NotifyCanExecuteChanged();
                }
            };
        }
    }
}
