using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Threading;
using XdkExplorer.ViewModels;

namespace XdkExplorer.Views;

/// <summary>
/// File list with Explorer-like type-ahead: keys typed in quick succession form a prefix, repeating one letter cycles
/// through the names starting with it, and a prefix without a match starts over with the last letter.
/// </summary>
public sealed class FileListBox : ListBox
{
    private static readonly TimeSpan TypeAheadTimeout = TimeSpan.FromSeconds(1);

    private string _prefix = "";
    private DateTime _lastInput;

    public FileListBox()
    {
        // Implicit styles are keyed by the exact type, so the Fluent ListBox style has to be picked up explicitly
        SetResourceReference(StyleProperty, typeof(ListBox));

        // The built-in search keeps growing a prefix that no longer matches and selects ranges while Shift is held
        IsTextSearchEnabled = false;
    }

    protected override void OnTextInput(TextCompositionEventArgs e)
    {
        var text = e.Text;
        var now = DateTime.UtcNow;
        bool continues = _prefix.Length > 0 && now - _lastInput < TypeAheadTimeout;

        // Control characters come from Enter, Backspace, Escape and Ctrl shortcuts. A space only counts inside a name.
        if (string.IsNullOrEmpty(text) || char.IsControl(text[0]) || (text == " " && !continues))
        {
            base.OnTextInput(e);

            return;
        }

        _lastInput = now;
        e.Handled = true;

        var prefix = continues ? _prefix + text : text;
        int current = GetCurrentIndex();

        // Extending the prefix may stay on the current item, a single letter moves on to the next one
        var match = Find(prefix, prefix.Length > 1 ? current : current + 1);

        if (match == null && prefix.Length > 1 && IsOneLetterRepeated(prefix))
        {
            match = Find(text, current + 1);
        }

        if (match == null && prefix.Length > 1)
        {
            prefix = text;
            match = Find(prefix, current + 1);
        }

        _prefix = prefix;

        if (match != null)
        {
            MoveTo(match);
        }
    }

    /// <summary>Selects only item, makes it the anchor for Shift selection and moves keyboard focus to its row.</summary>
    public void SelectAndFocus(object item)
    {
        // The list may still be collapsed from loading, rows exist only after the next layout pass
        Dispatcher.BeginInvoke(DispatcherPriority.Loaded, () =>
        {
            if (Items.Contains(item))
            {
                MoveTo(item);
            }
        });
    }

    private void MoveTo(object item)
    {
        SelectedItem = item;
        AnchorItem = item;
        ScrollIntoView(item);
        UpdateLayout();

        if (ItemContainerGenerator.ContainerFromItem(item) is ListBoxItem container)
        {
            container.Focus();
        }
    }

    /// <summary>The focused row counts, like in Explorer. Without one, the first selected item.</summary>
    private int GetCurrentIndex()
    {
        if (Keyboard.FocusedElement is ListBoxItem container)
        {
            int index = ItemContainerGenerator.IndexFromContainer(container);

            if (index >= 0)
            {
                return index;
            }
        }

        return SelectedIndex;
    }

    /// <summary>First item from start on, wrapping around, whose name starts with prefix.</summary>
    private object? Find(string prefix, int start)
    {
        int count = Items.Count;

        if (count == 0)
        {
            return null;
        }

        start = ((start % count) + count) % count;

        for (int i = 0; i < count; i++)
        {
            var item = Items[(start + i) % count];

            if (item is FileItemViewModel file && file.Name.StartsWith(prefix, StringComparison.OrdinalIgnoreCase))
            {
                return item;
            }
        }

        return null;
    }

    private static bool IsOneLetterRepeated(string prefix)
    {
        return prefix.All(c => char.ToUpperInvariant(c) == char.ToUpperInvariant(prefix[0]));
    }
}
