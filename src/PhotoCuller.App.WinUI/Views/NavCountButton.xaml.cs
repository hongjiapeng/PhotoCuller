using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;

namespace PhotoCuller.App.WinUI.Views;

public sealed partial class NavCountButton : UserControl
{
    public static readonly DependencyProperty GlyphProperty =
        DependencyProperty.Register(nameof(Glyph), typeof(string), typeof(NavCountButton),
            new PropertyMetadata(string.Empty, (d, e) =>
            {
                if (d is NavCountButton b) b.IconEl.Glyph = e.NewValue as string ?? string.Empty;
            }));

    public static readonly DependencyProperty LabelProperty =
        DependencyProperty.Register(nameof(Label), typeof(string), typeof(NavCountButton),
            new PropertyMetadata(string.Empty, (d, e) =>
            {
                if (d is NavCountButton b) b.LabelEl.Text = e.NewValue as string ?? string.Empty;
            }));

    public static readonly DependencyProperty CountProperty =
        DependencyProperty.Register(nameof(Count), typeof(int), typeof(NavCountButton),
            new PropertyMetadata(0, (d, e) =>
            {
                if (d is not NavCountButton b || e.NewValue is not int n) return;
                if (n > 0)
                {
                    b.CountEl.Text = n > 99999 ? "99999+" : n.ToString("N0");
                    b.CountEl.Visibility = Visibility.Visible;
                }
                else
                {
                    b.CountEl.Visibility = Visibility.Collapsed;
                }
            }));

    public static readonly DependencyProperty TagProperty =
        DependencyProperty.Register(nameof(Tag), typeof(string), typeof(NavCountButton),
            new PropertyMetadata(string.Empty));

    public event RoutedEventHandler? Click;

    public string Glyph
    {
        get => (string)GetValue(GlyphProperty);
        set => SetValue(GlyphProperty, value);
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

    public NavCountButton() => InitializeComponent();

    private void Root_Click(object sender, RoutedEventArgs e)
    {
        Root.Tag = Tag;
        Click?.Invoke(this, e);
    }
}
