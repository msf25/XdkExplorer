using XdkExplorer.Xbdm;

namespace XdkExplorer.ViewModels;

public sealed class Breadcrumb
{
    public Breadcrumb(string label, string? path, bool isLast)
    {
        Label = label;
        Path = path;
        IsLast = isLast;
    }

    public string Label { get; }

    /// <summary>Null for the console crumb, which leads to the overview.</summary>
    public string? Path { get; }

    public bool IsLast { get; }

    /// <summary>Console name followed by one crumb per path segment, e.g. Helmo › E: › DEVKIT.</summary>
    public static List<Breadcrumb> Build(string consoleName, string? path)
    {
        var crumbs = new List<Breadcrumb> { new(consoleName, null, path == null) };

        if (path == null)
        {
            return crumbs;
        }

        var parts = path.TrimEnd('\\').Split('\\');
        var current = "";

        for (int i = 0; i < parts.Length; i++)
        {
            current = i == 0 ? parts[0] + "\\" : XboxPath.Combine(current, parts[i]);

            crumbs.Add(new Breadcrumb(parts[i], current, i == parts.Length - 1));
        }

        return crumbs;
    }
}
