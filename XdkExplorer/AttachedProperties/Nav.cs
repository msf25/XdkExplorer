using System.Windows;

namespace XdkExplorer.AttachedProperties;

/// <summary>Selection flag for templated buttons in the sidebar, usable in style triggers.</summary>
public static class Nav
{
    public static readonly DependencyProperty IsSelectedProperty =
        DependencyProperty.RegisterAttached("IsSelected", typeof(bool), typeof(Nav), new PropertyMetadata(false));

    public static bool GetIsSelected(DependencyObject element)
    {
        return (bool)element.GetValue(IsSelectedProperty);
    }

    public static void SetIsSelected(DependencyObject element, bool value)
    {
        element.SetValue(IsSelectedProperty, value);
    }
}
