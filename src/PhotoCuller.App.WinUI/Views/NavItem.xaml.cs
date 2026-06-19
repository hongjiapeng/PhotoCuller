using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;

namespace PhotoCuller.App.WinUI.Views;

public sealed partial class NavItem : UserControl
{
    public static readonly DependencyProperty IconProperty =
        DependencyProperty.Register(nameof(Icon), typeof(string), typeof(NavItem),
            new PropertyMetadata("&#xE91B;", OnIconChanged));

    public static readonly DependencyProperty LabelProperty =
        DependencyProperty.Register(nameof(Label), typeof(string), typeof(NavItem),
            new PropertyMetadata(string.Empty, OnLabelChanged));

    public static readonly DependencyProperty CountProperty =
        DependencyProperty.Register(nameof(Count), typeof(int), typeof(NavItem),
            new PropertyMetadata(0, OnCountChanged));

    public static readonly DependencyProperty TagProperty =
        DependencyProperty.Register(nameof(Tag), typeof(string), typeof(NavItem),
            new PropertyMetadata(string.Empty));

    public event RoutedEventHandler? Click;

    public string Icon
    {
        get => (string)GetValue(IconProperty);
        set => SetValue(IconProperty, value);
    }

    public string Label
    {
        get => (string)GetValue(LabelProperty);
        set => SetValue(LabelProperty, value);
    }

    public int Count
    {
        get => (int)GetValue(CountProperty);
        set => SetValue(CountProperty, value);
    }

    public new string Tag
    {
        get => (string)GetValue(TagProperty);
        set => SetValue(TagProperty, value);
    }

    public NavItem() => InitializeComponent();

    private static void OnIconChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
    {
        if (d is NavItem nav && e.NewValue is string glyph)
            nav.IconEl.Glyph = glyph;
    }

    private static void OnLabelChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
    {
        if (d is NavItem nav && e.NewValue is string label)
            nav.LabelEl.Text = label;
    }

    private static void OnCountChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
    {
        if (d is not NavItem nav || e.NewValue is not int count) return;
        if (count > 0)
        {
            nav.CountEl.Text = count > 9999 ? "9999+" : count.ToString();
            nav.BadgeBorder.Visibility = Visibility.Visible;
        }
        else
        {
            nav.BadgeBorder.Visibility = Visibility.Collapsed;
        }
    }

    private void Root_Click(object sender, RoutedEventArgs e)
    {
        Root.Tag = Tag; // forward Tag for NavItem_Click in MainWindow
        Click?.Invoke(this, e);
    }
}
