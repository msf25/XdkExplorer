using System.Windows;
using System.Windows.Controls;

namespace XdkExplorer.Views;

public partial class ConsoleOverviewView : UserControl
{
    /// <summary>Height of whatever floats over the bottom of the page, so the last card stays reachable.</summary>
    public static readonly DependencyProperty BottomInsetProperty =
        DependencyProperty.Register(nameof(BottomInset), typeof(double), typeof(ConsoleOverviewView),
            new PropertyMetadata(0.0, (d, _) => ((ConsoleOverviewView)d).UpdatePageMargin()));

    public ConsoleOverviewView()
    {
        InitializeComponent();
    }

    public double BottomInset
    {
        get => (double)GetValue(BottomInsetProperty);
        set => SetValue(BottomInsetProperty, value);
    }

    private void UpdatePageMargin()
    {
        Page.Margin = new Thickness(24, 12, 24, 24 + BottomInset);
    }
}
