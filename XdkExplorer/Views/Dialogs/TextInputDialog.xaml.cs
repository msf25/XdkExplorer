using System.Windows;
using System.Windows.Controls;

namespace XdkExplorer.Views;

public partial class TextInputDialog : Window
{
    private readonly Func<string, string?> _validate;

    public TextInputDialog(string title, string label, string initialText, Func<string, string?> validate)
    {
        InitializeComponent();

        _validate = validate;

        Title = title;
        LabelText.Text = label;
        InputBox.Text = initialText;

        Loaded += (_, _) =>
        {
            InputBox.Focus();
            SelectNameWithoutExtension();
        };
    }

    public string Value => InputBox.Text.Trim();

    private void SelectNameWithoutExtension()
    {
        int dot = InputBox.Text.LastIndexOf('.');

        if (dot > 0)
        {
            InputBox.Select(0, dot);
        }
        else
        {
            InputBox.SelectAll();
        }
    }

    private void OnTextChanged(object sender, TextChangedEventArgs e)
    {
        var error = Value.Length == 0 ? null : _validate(Value);

        ErrorText.Text = error ?? "";
        ErrorText.Visibility = error == null ? Visibility.Collapsed : Visibility.Visible;
        OkButton.IsEnabled = Value.Length > 0 && error == null;
    }

    private void OnOk(object sender, RoutedEventArgs e)
    {
        DialogResult = true;
    }
}
