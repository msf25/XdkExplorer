namespace XdkExplorer.Xbdm;

/// <summary>Path helpers for console paths like E:\DEVKIT\Game. Unlike System.IO.Path these never touch the local file system.</summary>
public static class XboxPath
{
    /// <summary>FATX limit for a single file or directory name.</summary>
    public const int MaxNameLength = 42;

    public static string Combine(string directory, string name)
    {
        return directory.EndsWith('\\') ? directory + name : directory + "\\" + name;
    }

    /// <summary>Parent directory, or null for a drive root.</summary>
    public static string? GetParent(string path)
    {
        var trimmed = path.TrimEnd('\\');
        int slash = trimmed.LastIndexOf('\\');

        if (slash < 0)
        {
            return null;
        }

        // "E:\Dir" -> "E:\"
        return slash == 2 ? trimmed[..3] : trimmed[..slash];
    }

    public static string GetName(string path)
    {
        var trimmed = path.TrimEnd('\\');
        int slash = trimmed.LastIndexOf('\\');

        return slash < 0 ? trimmed : trimmed[(slash + 1)..];
    }

    /// <summary>
    /// Name of the entry in folder that leads to path, e.g. "Samples" for E:\ and E:\Samples\Gamepad.
    /// Null if path is not below folder.
    /// </summary>
    public static string? GetChildToward(string folder, string path)
    {
        var target = folder.TrimEnd('\\');
        string? current = path;

        while (current != null)
        {
            var parent = GetParent(current);

            if (parent != null && string.Equals(parent.TrimEnd('\\'), target, StringComparison.OrdinalIgnoreCase))
            {
                return GetName(current);
            }

            current = parent;
        }

        return null;
    }

    public static bool IsValidName(string name)
    {
        return name.Length > 0 && name.Length <= MaxNameLength && name.IndexOfAny(['\\', '/', ':', '*', '?', '"', '<', '>', '|']) < 0;
    }
}
