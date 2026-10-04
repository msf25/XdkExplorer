using System.IO;
using System.Windows;
using XdkExplorer.Diagnostics;
using XdkExplorer.Services;
using XdkExplorer.ViewModels;
using XdkExplorer.Views;
using XdkExplorer.Xbdm;

namespace XdkExplorer;

public partial class App : Application
{
    private XbdmWorker? _worker;

    protected override void OnStartup(StartupEventArgs e)
    {
        base.OnStartup(e);

        if (e.Args.Length > 0 && e.Args[0] == "--diagnose")
        {
            Shutdown(ConsoleDiagnostics.Run(e.Args));

            return;
        }

        // The wizard window must not end the application when it closes
        ShutdownMode = ShutdownMode.OnExplicitShutdown;

        bool isFirstStart = !File.Exists(AppSettings.FilePath);
        var settings = AppSettings.Load();
        var dll = settings.XdkDllPath != null
            ? XdkLocator.FromDll(settings.XdkDllPath)?.DllPath
            : XboxDbgNative.FindInstalledDll();

        if (isFirstStart || dll == null)
        {
            _worker = RunSetupWizard(settings);

            if (_worker == null)
            {
                Shutdown();

                return;
            }
        }
        else
        {
            XboxDbgNative.UseDll(dll);
            _worker = new XbdmWorker();
        }

        var viewModel = new MainViewModel(_worker, settings, new DialogService());
        var window = new MainWindow(viewModel);

        MainWindow = window;
        ShutdownMode = ShutdownMode.OnMainWindowClose;
        window.Show();
    }

    /// <summary>Returns the worker the wizard started, or null if the wizard was closed before it finished.</summary>
    private XbdmWorker? RunSetupWizard(AppSettings settings)
    {
        var wizard = new SetupWizardViewModel(settings, new DialogService());
        var window = new SetupWizard(wizard);

        MainWindow = window;
        window.ShowDialog();

        if (wizard.IsCompleted)
        {
            return wizard.Worker;
        }

        wizard.Worker?.Dispose();

        return null;
    }

    protected override void OnExit(ExitEventArgs e)
    {
        _worker?.Dispose();

        base.OnExit(e);
    }
}
