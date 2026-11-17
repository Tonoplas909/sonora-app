using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using Sonora.Appearance.Theming;

namespace Sonora.Views;

/// <summary>Swatch + hexadecimal field + hue/saturation/lightness sliders.</summary>
public partial class ColorPicker : UserControl
{
    public static readonly DependencyProperty HexProperty =
        DependencyProperty.Register(nameof(Hex), typeof(string), typeof(ColorPicker),
            new FrameworkPropertyMetadata("#000000", FrameworkPropertyMetadataOptions.BindsTwoWayByDefault, OnHexChanged));

    bool _syncing;

    public ColorPicker()
    {
        InitializeComponent();
        SyncSliders();
    }

    public string Hex
    {
        get => (string)GetValue(HexProperty);
        set => SetValue(HexProperty, value);
    }

    static void OnHexChanged(DependencyObject d, DependencyPropertyChangedEventArgs e) => ((ColorPicker)d).SyncSliders();

    void SyncSliders()
    {
        if (_syncing || !Argb.TryParse(Hex, out var c)) return;
        _syncing = true;
        var (h, s, l) = c.ToHsl();
        Hue.Value = h;
        Sat.Value = s;
        Lum.Value = l;
        _syncing = false;
    }

    void Hsl_Changed(object sender, RoutedPropertyChangedEventArgs<double> e)
    {
        if (_syncing || !Argb.TryParse(Hex, out var current)) return;
        _syncing = true;
        Hex = Argb.FromHsl(Hue.Value, Sat.Value, Lum.Value, current.A).ToHex();
        _syncing = false;
    }

    void HexBox_KeyDown(object sender, KeyEventArgs e)
    {
        if (e.Key != Key.Enter) return;
        HexBox.GetBindingExpression(TextBox.TextProperty)?.UpdateSource();
        e.Handled = true;
    }

    void Popup_Closed(object? sender, EventArgs e) => Swatch.IsChecked = false;
}
