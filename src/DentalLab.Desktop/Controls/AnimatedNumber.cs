using System.Globalization;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media.Animation;
using DentalLab.Desktop.Motion;

namespace DentalLab.Desktop.Controls;

/// <summary>A number that counts up to its value (instant when motion is reduced).</summary>
public sealed class AnimatedNumber : TextBlock
{
    public static readonly DependencyProperty ValueProperty = DependencyProperty.Register(
        nameof(Value), typeof(double), typeof(AnimatedNumber), new PropertyMetadata(0.0, OnValueChanged));

    public static readonly DependencyProperty DecimalsProperty = DependencyProperty.Register(
        nameof(Decimals), typeof(int), typeof(AnimatedNumber), new PropertyMetadata(0, (d, _) => ((AnimatedNumber)d).UpdateText()));

    private static readonly DependencyProperty ShownProperty = DependencyProperty.Register(
        "Shown", typeof(double), typeof(AnimatedNumber), new PropertyMetadata(0.0, (d, _) => ((AnimatedNumber)d).UpdateText()));

    public double Value { get => (double)GetValue(ValueProperty); set => SetValue(ValueProperty, value); }
    public int Decimals { get => (int)GetValue(DecimalsProperty); set => SetValue(DecimalsProperty, value); }

    public AnimatedNumber() => UpdateText();

    private static void OnValueChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
    {
        var c = (AnimatedNumber)d;
        var to = (double)e.NewValue;
        if (!MotionService.IsEnabled)
        {
            c.BeginAnimation(ShownProperty, null);
            c.SetValue(ShownProperty, to);
            return;
        }
        var from = (double)c.GetValue(ShownProperty);
        c.BeginAnimation(ShownProperty, new DoubleAnimation(from, to, new Duration(TimeSpan.FromMilliseconds(700)))
        { EasingFunction = MotionTokens.EaseOut });
    }

    private void UpdateText()
        => Text = ((double)GetValue(ShownProperty)).ToString("N" + Math.Max(0, Decimals), CultureInfo.InvariantCulture);
}
