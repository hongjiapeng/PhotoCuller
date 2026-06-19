using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;

namespace PhotoCuller.App.WinUI.Views;

public sealed partial class ScoreRow : UserControl
{
    public static readonly DependencyProperty LabelProperty =
        DependencyProperty.Register(nameof(Label), typeof(string), typeof(ScoreRow),
            new PropertyMetadata(string.Empty, (d, e) =>
            {
                if (d is ScoreRow r) r.LabelEl.Text = e.NewValue as string ?? string.Empty;
            }));

    public static readonly DependencyProperty ValueProperty =
        DependencyProperty.Register(nameof(Value), typeof(float?), typeof(ScoreRow),
            new PropertyMetadata(null, (d, e) =>
            {
                if (d is not ScoreRow r) return;
                if (e.NewValue is float f)
                {
                    r.BarEl.Value   = f;
                    r.ValueEl.Text  = ((int)Math.Round(f)).ToString();
                }
                else
                {
                    r.BarEl.Value  = 0;
                    r.ValueEl.Text = "—";
                }
            }));

    public string Label
    {
        get => (string)GetValue(LabelProperty);
        set => SetValue(LabelProperty, value);
    }

    public float? Value
    {
        get => (float?)GetValue(ValueProperty);
        set => SetValue(ValueProperty, value);
    }

    public ScoreRow() => InitializeComponent();
}
