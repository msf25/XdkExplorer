using System.Windows.Controls;

namespace XdkExplorer.Views;

public partial class ConsoleConnectForm : UserControl
{
    public ConsoleConnectForm()
    {
        InitializeComponent();

        Loaded += (_, _) => InputBox.Focus();
    }
}
