using System.IO;
using CommunityToolkit.Mvvm.ComponentModel;
using XdkExplorer.Services;
using XdkExplorer.Xbdm;

namespace XdkExplorer.ViewModels;

public sealed class FileItemViewModel : ObservableObject
{
    public FileItemViewModel(XboxFileEntry entry, string directory)
    {
        Entry = entry;
        FullPath = XboxPath.Combine(directory, entry.Name);
    }

    public XboxFileEntry Entry { get; private set; }

    public string FullPath { get; }

    public string Name => Entry.Name;

    public bool IsDirectory => Entry.IsDirectory;

    public bool IsXbe => !IsDirectory && Name.EndsWith(".xbe", StringComparison.OrdinalIgnoreCase);

    public bool IsFile => !IsDirectory && !IsXbe;

    public ulong Size => IsDirectory ? 0 : Entry.Size;

    public string SizeText => IsDirectory ? "" : Format.Size(Entry.Size);

    public string ModifiedText => Entry.Modified == DateTime.MinValue ? "" : Entry.Modified.ToString("g");

    public string TypeText => DescribeType(this);

    /// <summary>Takes over new size, date or attributes of the same file without replacing the item, so it stays selected.</summary>
    public void Update(XboxFileEntry entry)
    {
        Entry = entry;

        // Empty name refreshes every binding on this item
        OnPropertyChanged(string.Empty);
    }

    private static string DescribeType(FileItemViewModel item)
    {
        if (item.IsDirectory)
        {
            return "File folder";
        }

        var extension = Path.GetExtension(item.Name).ToLowerInvariant();

        switch (extension)
        {
            case ".xbe": return "Xbox executable";
            case ".xbx": return "Xbox image";
            case ".xpr": return "XPR resource";
            case ".xmv": return "Xbox video";
            case ".wma": return "Audio";
            case ".xwb": return "Wave bank";
            case ".xsb": return "Sound bank";
            case ".ini": return "Configuration";
            case ".log":
            case ".txt": return "Text document";
            case "": return "File";
            default: return $"{extension[1..].ToUpperInvariant()} file";
        }
    }
}
