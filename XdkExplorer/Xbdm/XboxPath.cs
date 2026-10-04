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

    public static bool IsValidName(string name)
    {
        return name.Length > 0 && name.Length <= MaxNameLength && name.IndexOfAny(['\\', '/', ':', '*', '?', '"', '<', '>', '|']) < 0;
    }
}
