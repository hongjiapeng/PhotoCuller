using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Controls.Primitives;

namespace PhotoCuller.App.WinUI.Views;

public sealed partial class WeightRow : UserControl
{
    public static readonly DependencyProperty LabelProperty =
        DependencyProperty.Register(nameof(Label), typeof(string), typeof(WeightRow),
            new PropertyMetadata(string.Empty, (d, e) =>
            {
                if (d is WeightRow r) r.LabelEl.Text = e.NewValue as string ?? string.Empty;
            }));

    public static readonly DependencyProperty ValueProperty =
        DependencyProperty.Register(nameof(Value), typeof(int), typeof(WeightRow),
            new PropertyMetadata(0, (d, e) =>
            {
                if (d is not WeightRow r || e.NewValue is not int v) return;
                r._updating = true;
                r.SliderEl.Value = v;
                r.ValueEl.Text   = $"{v}%";
                r._updating = false;
            }));

    private bool _updating;

    public string Label
    {
        get => (string)GetValue(LabelProperty);
        set => SetValue(LabelProperty, value);
    }

    public int Value
    {
        get => (int)GetValue(ValueProperty);
        set => SetValue(ValueProperty, value);
    }

    public WeightRow() => InitializeComponent();

    private void Slider_ValueChanged(object sender, RangeBaseValueChangedEventArgs e)
    {
        if (_updating) return;
        ValueEl.Text = $"{(int)e.NewValue}%";
        Value = (int)e.NewValue;
    }
}
