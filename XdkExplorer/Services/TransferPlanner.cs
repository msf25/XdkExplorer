using System.IO;
using XdkExplorer.Xbdm;

namespace XdkExplorer.Services;

/// <summary>A top-level file or folder the user picked for a transfer.</summary>
public sealed class TransferSource
{
    public TransferSource(string path, bool isDirectory, ulong size)
    {
        Path = path;
        IsDirectory = isDirectory;
        Size = size;
    }

    public string Path { get; }

    public bool IsDirectory { get; }

    public ulong Size { get; }
}

/// <summary>One file to copy, with full source and target path.</summary>
public sealed class TransferItem
{
    public TransferItem(string source, string target, long size)
    {
        Source = source;
        Target = target;
        Size = size;
    }

    public string Source { get; }

    public string Target { get; }

    public long Size { get; }
}

/// <summary>Expands picked files and folders into single files and creates the target folder structure.</summary>
public static class TransferPlanner
{
    public static async Task<List<TransferItem>> PlanUploadAsync(XbdmWorker worker, string console,
        IEnumerable<TransferSource> sources, string remoteDirectory)
    {
        var items = new List<TransferItem>();
        var remoteDirectories = new List<string>();

        foreach (var source in sources)
        {
            var remoteRoot = XboxPath.Combine(remoteDirectory, Path.GetFileName(source.Path.TrimEnd('\\')));

            if (!source.IsDirectory)
            {
                items.Add(new TransferItem(source.Path, remoteRoot, new FileInfo(source.Path).Length));

                continue;
            }

            remoteDirectories.Add(remoteRoot);

            foreach (var directory in Directory.EnumerateDirectories(source.Path, "*", SearchOption.AllDirectories))
            {
                remoteDirectories.Add(XboxPath.Combine(remoteRoot, Path.GetRelativePath(source.Path, directory)));
            }

            foreach (var file in Directory.EnumerateFiles(source.Path, "*", SearchOption.AllDirectories))
            {
                var remote = XboxPath.Combine(remoteRoot, Path.GetRelativePath(source.Path, file));

                items.Add(new TransferItem(file, remote, new FileInfo(file).Length));
            }
        }

        // EnumerateDirectories lists parents before children, so creation order is safe
        await worker.RunAsync(console, () =>
        {
            foreach (var directory in remoteDirectories)
            {
                XbdmCommands.CreateDirectory(directory);
            }
        });

        return items;
    }

    public static async Task<List<TransferItem>> PlanDownloadAsync(XbdmWorker worker, string console,
        IEnumerable<TransferSource> sources, string localDirectory)
    {
        var items = new List<TransferItem>();

        foreach (var source in sources)
        {
            var localRoot = Path.Combine(localDirectory, XboxPath.GetName(source.Path));

            if (!source.IsDirectory)
            {
                items.Add(new TransferItem(source.Path, localRoot, (long)source.Size));

                continue;
            }

            var (files, directories) = await worker.RunAsync(console, () => XbdmCommands.ListRecursive(source.Path));

            foreach (var directory in directories)
            {
                Directory.CreateDirectory(Path.Combine(localRoot, Path.GetRelativePath(source.Path, directory)));
            }

            foreach (var (path, entry) in files)
            {
                items.Add(new TransferItem(path, Path.Combine(localRoot, Path.GetRelativePath(source.Path, path)), (long)entry.Size));
            }
        }

        return items;
    }
}
