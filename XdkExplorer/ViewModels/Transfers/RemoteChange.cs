namespace XdkExplorer.ViewModels;

/// <summary>Something in a directory on a console was created, finished or removed by a transfer.</summary>
public sealed class RemoteChange
{
    public RemoteChange(ConsoleViewModel console, string directory)
    {
        Console = console;
        Directory = directory;
    }

    public ConsoleViewModel Console { get; }

    public string Directory { get; }
}
