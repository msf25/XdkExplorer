using System.Diagnostics;
using System.IO;
using System.Security.Cryptography;
using System.Text;
using XdkExplorer.Services;
using XdkExplorer.Xbdm;

namespace XdkExplorer.Diagnostics;

/// <summary>
/// Checks the connection to a console and file transfers, for users and for measuring transfer speeds.
/// Usage: XdkExplorer.exe --diagnose [size in MB] [console name or IP], arguments in any order.
/// Output goes to the console and to diagnose.log next to settings.json.
/// </summary>
internal static class ConsoleDiagnostics
{
    private const string RemoteDir = @"E:\XdkExplorerTest";
    private const int ChunkSize = 256 * 1024;
    private const int DefaultSizeMb = 4;

    private static readonly object _logLock = new();
    private static readonly List<string> _problems = new();
    private static StreamWriter? _log;

    public static int Run(string[] args)
    {
        ConsoleWindow.Connect();

        ParseArguments(args, out int sizeMb, out string? target);

        var logPath = Path.Combine(Path.GetDirectoryName(AppSettings.FilePath)!, "diagnose.log");

        Directory.CreateDirectory(Path.GetDirectoryName(logPath)!);

        int exitCode;

        using (_log = new StreamWriter(logPath, false, Encoding.UTF8) { AutoFlush = true })
        {
            try
            {
                Execute(sizeMb, target, logPath);
            }
            catch (XbdmException ex)
            {
                // Expected failures such as an unreachable console, the message says enough
                _problems.Add($"Stopped: {ex.Message}");
            }
            catch (Exception ex)
            {
                _problems.Add($"Stopped with an error: {ex.Message}");
                Log($"\n{ex}");
            }

            if (_problems.Count == 0)
            {
                Log("\nRESULT: OK");
                exitCode = 0;
            }
            else
            {
                Log("\nRESULT: FAILED");

                foreach (var problem in _problems)
                {
                    Log($"  - {problem}");
                }

                exitCode = 1;
            }

            Log($"\nLog: {logPath}");
        }

        ConsoleWindow.WaitIfOwnWindow();

        return exitCode;
    }

    /// <summary>A number is the test file size in MB, anything else the console.</summary>
    private static void ParseArguments(string[] args, out int sizeMb, out string? target)
    {
        sizeMb = DefaultSizeMb;
        target = null;

        foreach (var arg in args.Skip(1))
        {
            if (int.TryParse(arg, out int mb) && mb > 0)
            {
                sizeMb = mb;
            }
            else
            {
                target = arg;
            }
        }
    }

    private static void Execute(int sizeMb, string? target, string logPath)
    {
        Log($"XDK Explorer {AppInfo.Version} diagnostics  {DateTime.Now:yyyy-MM-dd HH:mm:ss}  ({AppInfo.Runtime})");
        Log("Usage: XdkExplorer.exe --diagnose [size in MB] [console name or IP]");
        Log("");
        Log("Checks the connection to a console and file transfers:");
        Log("  - console name, address and drives");
        Log($"  - upload and download of a {sizeMb} MB random test file, compared by SHA256");
        Log("  - an upload cancelled in the middle, which must not leave a file behind");
        Log($"The test writes to {RemoteDir} on the console and deletes the folder at the end.");
        Log($"Everything shown here is also written to {logPath}.");

        // Same DLL as the app: the one chosen in the settings, otherwise the installed XDK
        var settings = AppSettings.Load();
        var dll = settings.XdkDllPath != null
            ? XdkLocator.FromDll(settings.XdkDllPath)?.DllPath
            : XboxDbgNative.FindInstalledDll();

        if (dll == null)
        {
            throw new FileNotFoundException("xboxdbg.dll was not found. Start XDK Explorer normally once to set up the XDK.");
        }

        Log($"\nDLL:          {dll}");

        XboxDbgNative.UseDll(dll);

        using var worker = new XbdmWorker();

        var defaultXbox = Wait(worker.RunAsync(null, XbdmCommands.GetDefaultXboxName));
        var console = target ?? defaultXbox;

        Log($"Default Xbox: {(string.IsNullOrEmpty(defaultXbox) ? "(none)" : defaultXbox)}");

        if (string.IsNullOrEmpty(console))
        {
            throw new InvalidOperationException("There is no default console. Pass a console name or IP address.");
        }

        Log($"Target:       {console}");
        Log($"\nConnecting to {console} ...");

        var sw = Stopwatch.StartNew();
        var address = Wait(worker.RunAsync(console, XbdmCommands.ResolveAddress));

        Log($"Address:      {address}  ({sw.ElapsedMilliseconds} ms)");
        Log($"Reports name: {Wait(worker.RunAsync(console, XbdmCommands.GetConsoleName))}");
        Log($"XBDM version: {Wait(worker.RunAsync(console, XbdmCommands.GetDebugMonitorVersion)) ?? "older than 4831"}");

        LogDrives(worker, console);
        LogListing(worker, console);

        var tmp = Path.Combine(Path.GetTempPath(), "XdkExplorerTest");

        Directory.CreateDirectory(tmp);

        try
        {
            TestTransfers(worker, console, tmp, sizeMb);
        }
        finally
        {
            CleanUp(worker, console, tmp);
        }
    }

    private static void LogDrives(XbdmWorker worker, string console)
    {
        Log("\nDrives:");

        foreach (var drive in Wait(worker.RunAsync(console, XbdmCommands.GetDrives)))
        {
            try
            {
                var (free, total) = Wait(worker.RunAsync(console, () => XbdmCommands.GetDiskSpace(drive)));

                Log($"  {drive}:  {Mb(free),10} free of {Mb(total),10}");
            }
            catch (XbdmException ex)
            {
                Log($"  {drive}:  {ex.Message}");
            }
        }
    }

    private static void LogListing(XbdmWorker worker, string console)
    {
        var sw = Stopwatch.StartNew();
        var entries = Wait(worker.RunAsync(console, () => XbdmCommands.ListDirectory(@"E:\")));

        Log($"\nE:\\ has {entries.Count} entries ({sw.ElapsedMilliseconds} ms)");

        foreach (var entry in entries)
        {
            Log($"  {(entry.IsDirectory ? "<DIR>" : Mb(entry.Size)),12}  {entry.Modified:yyyy-MM-dd HH:mm}  {entry.Name}");
        }
    }

    private static void TestTransfers(XbdmWorker worker, string console, string tmp, int sizeMb)
    {
        var localSource = Path.Combine(tmp, "upload.bin");
        var data = new byte[sizeMb * 1024 * 1024];

        Random.Shared.NextBytes(data);
        File.WriteAllBytes(localSource, data);

        var sourceHash = Convert.ToHexString(SHA256.HashData(data));

        Log($"\nTest file: {sizeMb} MB random, SHA256 {sourceHash[..16]}...");

        Wait(worker.RunAsync(console, () => XbdmCommands.CreateDirectory(RemoteDir)));

        // Upload with a directory listing in the middle of it
        Log($"\n[1/3] Upload ({ChunkSize / 1024} KB blocks), listing E:\\ at 50 % to check the app stays responsive");

        var remoteFile = RemoteDir + @"\upload.bin";
        var listingStarted = 0;
        Task<long>? listingTask = null;
        var sw = Stopwatch.StartNew();
        var logUploadProgress = ProgressLogger(sw);

        var upload = worker.RunTransferAsync(console, new FileUpload(localSource, remoteFile, ChunkSize), (sent, total) =>
        {
            logUploadProgress(sent, total);

            if (sent * 2 >= total && Interlocked.Exchange(ref listingStarted, 1) == 0)
            {
                var requested = Stopwatch.StartNew();

                listingTask = worker.RunAsync(console, () =>
                {
                    XbdmCommands.ListDirectory(@"E:\");

                    return requested.ElapsedMilliseconds;
                });
            }
        });

        Wait(upload);
        LogRate("    done", data.Length, sw.Elapsed);

        if (listingTask != null)
        {
            Log($"    listing during upload answered after {Wait(listingTask)} ms");
        }

        var remote = Wait(worker.RunAsync(console, () => XbdmCommands.GetAttributes(remoteFile)));

        if (remote?.Size == (ulong)data.Length)
        {
            Log($"    size on the console matches ({remote.Size} bytes)");
        }
        else
        {
            Log($"    SIZE MISMATCH: {remote?.Size.ToString() ?? "no file"} bytes on the console, {data.Length} sent");
            _problems.Add("The uploaded file has the wrong size on the console.");
        }

        // Download and verify
        Log("\n[2/3] Download and compare with the original");

        var localTarget = Path.Combine(tmp, "download.bin");

        sw.Restart();

        Wait(worker.RunTransferAsync(console, new FileDownload(remoteFile, localTarget, ChunkSize), ProgressLogger(sw)));

        LogRate("    done", data.Length, sw.Elapsed);

        var targetHash = Convert.ToHexString(SHA256.HashData(File.ReadAllBytes(localTarget)));

        if (targetHash == sourceHash)
        {
            Log("    SHA256 matches, the file arrived unchanged");
        }
        else
        {
            Log($"    SHA256 MISMATCH {targetHash[..16]}...");
            _problems.Add("The downloaded file differs from the uploaded one.");
        }

        TestCancel(worker, console, localSource);
    }

    /// <summary>A cancelled upload must not leave a partial file behind.</summary>
    private static void TestCancel(XbdmWorker worker, string console, string localSource)
    {
        Log("\n[3/3] Upload cancelled from outside at about 25 %");

        var cancelledFile = RemoteDir + @"\cancelled.bin";
        using var cts = new CancellationTokenSource();
        using var quarterSent = new ManualResetEventSlim();
        var sinceCancel = new Stopwatch();

        // Triggered by progress instead of time, so the cancel lands mid-transfer on slow and fast connections alike
        var cancelledUpload = new FileUpload(localSource, cancelledFile, ChunkSize);
        var cancelled = worker.RunTransferAsync(console, cancelledUpload, (sent, total) =>
        {
            if (sent * 4 >= total)
            {
                quarterSent.Set();
            }
        }, cts.Token);

        WaitHandle.WaitAny([quarterSent.WaitHandle, ((IAsyncResult)cancelled).AsyncWaitHandle]);

        sinceCancel.Start();
        cts.Cancel();

        try
        {
            Wait(cancelled);

            // Only possible if the last block went out before the cancel arrived
            Log("    upload finished before the cancel, nothing to check (try a bigger test file)");

            return;
        }
        catch (OperationCanceledException)
        {
            Log($"    transfer stopped {sinceCancel.ElapsedMilliseconds} ms after cancel (block size {ChunkSize / 1024} KB)");
        }

        bool isRemoved = Wait(worker.RemovePartialTargetAsync(console, cancelledUpload, TimeSpan.FromSeconds(15)));

        Log($"    partial file {(isRemoved ? "gone" : "still there")} {sinceCancel.ElapsedMilliseconds} ms after cancel");

        var leftover = Wait(worker.RunAsync(console, () => XbdmCommands.GetAttributes(cancelledFile)));

        if (leftover != null)
        {
            Log($"    PARTIAL FILE LEFT ({leftover.Size} bytes)");
            _problems.Add("A cancelled upload left a partial file on the console.");
        }
    }

    /// <summary>Best effort, also after a failed step. A leftover folder is reported but does not hide the original error.</summary>
    private static void CleanUp(XbdmWorker worker, string console, string tmp)
    {
        Log("\nCleaning up ...");

        try
        {
            Wait(worker.RunAsync(console, () => XbdmCommands.DeleteRecursive(RemoteDir, true)));
            Log($"    {RemoteDir} deleted on the console");
        }
        catch (XbdmException ex)
        {
            Log($"    could not delete {RemoteDir}: {ex.Message}");
            _problems.Add($"{RemoteDir} is still on the console and can be deleted by hand.");
        }

        try
        {
            Directory.Delete(tmp, true);
        }
        catch (IOException)
        {
            // Only temp files, Windows cleans them up eventually
        }
    }

    /// <summary>Progress callback that logs every 10 %, so a big test file does not flood the console.</summary>
    private static Action<long, long> ProgressLogger(Stopwatch sw)
    {
        int lastStep = -1;

        return (done, total) =>
        {
            int step = (int)(done * 10 / Math.Max(total, 1));

            if (step == lastStep)
            {
                return;
            }

            lastStep = step;
            Log($"    {done * 100 / Math.Max(total, 1),3} %  {Mb((ulong)done),10}  {Rate(done, sw.Elapsed)}");
        };
    }

    private static T Wait<T>(Task<T> task)
    {
        return task.GetAwaiter().GetResult();
    }

    private static void Wait(Task task)
    {
        task.GetAwaiter().GetResult();
    }

    private static void Log(string line)
    {
        lock (_logLock)
        {
            Console.WriteLine(line);

            _log?.WriteLine(line);
        }
    }

    private static void LogRate(string label, long bytes, TimeSpan elapsed)
    {
        Log($"{label}: {Mb((ulong)bytes)} in {elapsed.TotalSeconds:F1} s  ->  {Rate(bytes, elapsed)}");
    }

    private static string Rate(long bytes, TimeSpan elapsed) => $"{bytes / 1024.0 / 1024.0 / Math.Max(elapsed.TotalSeconds, 0.001):F2} MB/s";

    private static string Mb(ulong bytes) => $"{bytes / 1024.0 / 1024.0:F2} MB";
}
