namespace XdkExplorer.ViewModels;

/// <summary>
/// Back and forward stacks like in a web browser. Visiting a new location clears the forward stack.
/// A null location is the console overview.
/// </summary>
public sealed class NavigationHistory
{
    private readonly Stack<string?> _back = new();
    private readonly Stack<string?> _forward = new();

    public bool CanGoBack => _back.Count > 0;

    public bool CanGoForward => _forward.Count > 0;

    public void Visit(string? current, string? next)
    {
        if (string.Equals(current, next, StringComparison.OrdinalIgnoreCase))
        {
            return;
        }

        _back.Push(current);
        _forward.Clear();
    }

    public string? GoBack(string? current)
    {
        _forward.Push(current);

        return _back.Pop();
    }

    public string? GoForward(string? current)
    {
        _back.Push(current);

        return _forward.Pop();
    }

    public void Clear()
    {
        _back.Clear();
        _forward.Clear();
    }
}
