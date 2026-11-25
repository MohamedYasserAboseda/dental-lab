using System.Globalization;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Animation;
using System.Windows.Shapes;
using DentalLab.Desktop.Motion;

namespace DentalLab.Desktop.Controls;

public sealed record ChartSeries(string Name, string BrushKey, IReadOnlyList<double> Values);
public sealed record BarChartData(IReadOnlyList<string> Labels, IReadOnlyList<ChartSeries> Series);

/// <summary>Native vector bar chart (1 or 2 series). Bars grow in when the data changes; theme colours follow the app theme.</summary>
public sealed class BarChartControl : Grid
{
    public static readonly DependencyProperty DataProperty = DependencyProperty.Register(
        nameof(Data), typeof(BarChartData), typeof(BarChartControl),
        new PropertyMetadata(null, (d, _) => ((BarChartControl)d).Rebuild(animate: true)));

    public BarChartData? Data { get => (BarChartData?)GetValue(DataProperty); set => SetValue(DataProperty, value); }

    private readonly Canvas _canvas = new();

    public BarChartControl()
    {
        FlowDirection = FlowDirection.LeftToRight; // time axis always runs left to right
        ClipToBounds = true;
        Children.Add(_canvas);
        SizeChanged += (_, _) => Rebuild(animate: false);
    }

    private void Rebuild(bool animate)
    {
        _canvas.Children.Clear();
        var data = Data;
        double w = ActualWidth, h = ActualHeight;
        if (data is null || data.Labels.Count == 0 || data.Series.Count == 0 || w < 120 || h < 100) return;

        var multi = data.Series.Count > 1;
        double left = 52, right = 8, top = multi ? 30 : 22, bottom = 26;
        double pw = w - left - right, ph = h - top - bottom;
        var max = NiceMax(data.Series.SelectMany(s => s.Values).DefaultIfEmpty(0).Max());

        for (var i = 0; i <= 4; i++)
        {
            var y = top + ph - ph * i / 4;
            var line = new Line { X1 = left, X2 = left + pw, Y1 = y, Y2 = y, StrokeThickness = 1 };
            line.SetResourceReference(Shape.StrokeProperty, "BorderBrush");
            _canvas.Children.Add(line);

            var t = Text(Compact(max * i / 4), 10);
            t.Width = left - 8; t.TextAlignment = TextAlignment.Right;
            Canvas.SetLeft(t, 0); Canvas.SetTop(t, y - 8);
            _canvas.Children.Add(t);
        }

        var n = data.Labels.Count;
        var groupW = pw / n;
        var seriesCount = data.Series.Count;
        var barW = Math.Min(34, groupW * 0.72 / seriesCount);

        for (var g = 0; g < n; g++)
        {
            var label = Text(data.Labels[g], 11);
            label.Width = groupW; label.TextAlignment = TextAlignment.Center;
            Canvas.SetLeft(label, left + g * groupW); Canvas.SetTop(label, top + ph + 6);
            _canvas.Children.Add(label);

            for (var s = 0; s < seriesCount; s++)
            {
                var series = data.Series[s];
                var value = g < series.Values.Count ? series.Values[g] : 0;
                var height = max > 0 && value > 0 ? Math.Max(2, ph * value / max) : 0;
                var x = left + g * groupW + (groupW - barW * seriesCount) / 2 + s * barW;

                if (height > 0)
                {
                    var bar = new Rectangle { Width = Math.Max(2, barW - 3), Height = height, RadiusX = 4, RadiusY = 4 };
                    bar.SetResourceReference(Shape.FillProperty, series.BrushKey);
                    Canvas.SetLeft(bar, x + 1.5); Canvas.SetTop(bar, top + ph - height);

                    if (animate && MotionService.IsEnabled)
                    {
                        var scale = new ScaleTransform(1, 0);
                        bar.RenderTransformOrigin = new Point(0.5, 1);
                        bar.RenderTransform = scale;
                        scale.BeginAnimation(ScaleTransform.ScaleYProperty, new DoubleAnimation(0, 1, new Duration(TimeSpan.FromMilliseconds(500)))
                        {
                            BeginTime = TimeSpan.FromMilliseconds(g * 60 + s * 30),
                            EasingFunction = MotionTokens.EaseOut
                        });
                    }
                    _canvas.Children.Add(bar);
                }

                if (!multi && value > 0)
                {
                    var v = Text(Compact(value), 11);
                    v.Width = barW + 20; v.TextAlignment = TextAlignment.Center;
                    Canvas.SetLeft(v, x - 10); Canvas.SetTop(v, top + ph - height - 18);
                    _canvas.Children.Add(v);
                }
            }
        }

        if (multi)
        {
            double lx = left;
            foreach (var s in data.Series)
            {
                var swatch = new Rectangle { Width = 10, Height = 10, RadiusX = 2, RadiusY = 2 };
                swatch.SetResourceReference(Shape.FillProperty, s.BrushKey);
                Canvas.SetLeft(swatch, lx); Canvas.SetTop(swatch, 8);
                var name = Text(s.Name, 11);
                Canvas.SetLeft(name, lx + 15); Canvas.SetTop(name, 4);
                _canvas.Children.Add(swatch); _canvas.Children.Add(name);
                lx += 15 + s.Name.Length * 7 + 22;
            }
        }
    }

    private static TextBlock Text(string text, double size)
    {
        var t = new TextBlock { Text = text, FontSize = size, IsHitTestVisible = false };
        t.SetResourceReference(TextBlock.ForegroundProperty, "TextMutedBrush");
        return t;
    }

    private static string Compact(double v)
        => v >= 1_000_000 ? (v / 1_000_000).ToString("0.#", CultureInfo.InvariantCulture) + "M"
         : v >= 1_000 ? (v / 1_000).ToString("0.#", CultureInfo.InvariantCulture) + "K"
         : v.ToString("0.##", CultureInfo.InvariantCulture);

    /// <summary>Rounds the axis maximum up to 1, 2, 5 x 10^n so grid labels stay readable.</summary>
    private static double NiceMax(double max)
    {
        if (max <= 0) return 1;
        var exp = Math.Pow(10, Math.Floor(Math.Log10(max)));
        var f = max / exp;
        return (f <= 1 ? 1 : f <= 2 ? 2 : f <= 5 ? 5 : 10) * exp;
    }
}
