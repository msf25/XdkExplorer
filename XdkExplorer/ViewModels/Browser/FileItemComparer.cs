using System.Collections;

namespace XdkExplorer.ViewModels;

public enum SortColumn
{
    Name,
    Size,
    Modified,
    Type
}

/// <summary>Folders always come first, like in Windows Explorer. Ties are sorted by name.</summary>
public sealed class FileItemComparer : IComparer
{
    private readonly SortColumn _column;
    private readonly int _direction;

    public FileItemComparer(SortColumn column, bool ascending)
    {
        _column = column;
        _direction = ascending ? 1 : -1;
    }

    public int Compare(object? x, object? y)
    {
        var a = (FileItemViewModel)x!;
        var b = (FileItemViewModel)y!;

        if (a.IsDirectory != b.IsDirectory)
        {
            return a.IsDirectory ? -1 : 1;
        }

        int result = _column switch
        {
            SortColumn.Size => a.Size.CompareTo(b.Size),
            SortColumn.Modified => a.Entry.Modified.CompareTo(b.Entry.Modified),
            SortColumn.Type => string.Compare(a.TypeText, b.TypeText, StringComparison.OrdinalIgnoreCase),
            _ => 0
        };

        if (result == 0)
        {
            result = string.Compare(a.Name, b.Name, StringComparison.OrdinalIgnoreCase);
        }

        return result * _direction;
    }
}
