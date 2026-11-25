using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Animation;
using System.Windows.Shapes;
using Path = System.Windows.Shapes.Path;
using DentalLab.Desktop.Motion;
using DentalLab.Desktop.Services;
using DentalLab.Domain.Dental;

namespace DentalLab.Desktop.Controls;

/// <summary>
/// Reusable vector dental chart (FDI). Bind <see cref="SelectedTeeth"/> two-way to a list of FDI numbers.
/// Permanent and primary dentitions are drawn separately; the selection is shared between them.
/// </summary>
public partial class DentalChartControl : UserControl
{
    public static readonly DependencyProperty SelectedTeethProperty = DependencyProperty.Register(
        nameof(SelectedTeeth), typeof(IReadOnlyList<int>), typeof(DentalChartControl),
        new FrameworkPropertyMetadata(Array.Empty<int>(), FrameworkPropertyMetadataOptions.BindsTwoWayByDefault, OnSelectedTeethChanged));

    public static readonly DependencyProperty IsReadOnlyProperty = DependencyProperty.Register(
        nameof(IsReadOnly), typeof(bool), typeof(DentalChartControl),
        new PropertyMetadata(false, (d, _) => ((DentalChartControl)d).ApplyReadOnly()));

    public IReadOnlyList<int> SelectedTeeth
    {
        get => (IReadOnlyList<int>)GetValue(SelectedTeethProperty);
        set => SetValue(SelectedTeethProperty, value);
    }

    public bool IsReadOnly
    {
        get => (bool)GetValue(IsReadOnlyProperty);
        set => SetValue(IsReadOnlyProperty, value);
    }

    private readonly HashSet<int> _selected = [];
    private readonly Dictionary<int, ToothVisual> _visuals = [];
    private bool _updating;

    public DentalChartControl()
    {
        InitializeComponent();

        var group = "Dentition_" + Guid.NewGuid().ToString("N");
        PermanentRadio.GroupName = group;
        PrimaryRadio.GroupName = group;
        PermanentRadio.Checked += (_, _) => ShowDentition(Dentition.Permanent);
        PrimaryRadio.Checked += (_, _) => ShowDentition(Dentition.Primary);

        foreach (var (canvas, dentition) in new[] { (PermanentCanvas, Dentition.Permanent), (PrimaryCanvas, Dentition.Primary) })
        {
            BuildArch(canvas, dentition, upper: true);
            BuildArch(canvas, dentition, upper: false);
        }

        Loaded += (_, _) => { LocalizationService.Instance.PropertyChanged += OnLanguageChanged; RefreshSummary(); };
        Unloaded += (_, _) => LocalizationService.Instance.PropertyChanged -= OnLanguageChanged;
        ApplyReadOnly();
        RefreshSummary();
    }

    // ---------------- building ----------------

    private void BuildArch(Canvas canvas, Dentition dentition, bool upper)
    {
        var order = Fdi.ArchOrder(dentition, upper);
        var primary = dentition == Dentition.Primary;
        double cx = 380, cy = upper ? 232 : 248;
        double rx = primary ? 232 : 300, ry = primary ? 150 : 190;
        var scale = primary ? 0.9 : 1.0;

        for (var i = 0; i < order.Count; i++)
        {
            var info = Fdi.Get(order[i]);
            var theta = Math.PI * (1 - (i + 0.5) / order.Count);
            var x = cx + rx * Math.Cos(theta);
            var y = upper ? cy - ry * Math.Sin(theta) : cy + ry * Math.Sin(theta);

            // outward normal of the ellipse; the tooth root points along it
            var nx = Math.Cos(theta) / rx;
            var ny = (upper ? -Math.Sin(theta) : Math.Sin(theta)) / ry;
            var len = Math.Sqrt(nx * nx + ny * ny);
            nx /= len; ny /= len;
            var rotation = Math.Atan2(nx, -ny) * 180 / Math.PI;

            var (w, h) = info.Type switch
            {
                ToothType.Incisor => (34.0, 58.0),
                ToothType.Canine => (36.0, 60.0),
                ToothType.Premolar => (40.0, 58.0),
                _ => (48.0, 58.0)
            };
            w *= scale; h *= scale;

            var label = new TextBlock
            {
                Text = info.Number.ToString(), FontSize = 12, Width = 28, Height = 18,
                TextAlignment = TextAlignment.Center, IsHitTestVisible = false
            };
            label.SetResourceReference(TextBlock.ForegroundProperty, "TextMutedBrush");
            Canvas.SetLeft(label, x - nx * 46 - 14);
            Canvas.SetTop(label, y - ny * 46 - 9);
            canvas.Children.Add(label);

            var tooth = new ToothVisual(info, w, h, rotation, label);
            tooth.Clicked += OnToothClicked;
            tooth.HoverChanged += OnToothHover;
            Canvas.SetLeft(tooth, x - w / 2);
            Canvas.SetTop(tooth, y - h / 2);
            canvas.Children.Add(tooth);
            _visuals[info.Number] = tooth;
        }
    }

    // ---------------- selection ----------------

    private static void OnSelectedTeethChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
    {
        var c = (DentalChartControl)d;
        if (!c._updating) c.SyncFromProperty();
    }

    private void SyncFromProperty()
    {
        _selected.Clear();
        foreach (var n in SelectedTeeth ?? []) if (Fdi.IsValid(n)) _selected.Add(n);
        foreach (var (n, v) in _visuals) v.SetSelected(_selected.Contains(n), animate: false);
        RefreshSummary();
    }

    private void OnToothClicked(int number)
    {
        if (IsReadOnly) return;
        var on = _selected.Add(number);
        if (!on) _selected.Remove(number);
        _visuals[number].SetSelected(on, animate: true);
        Commit();
    }

    private void OnClearClicked(object sender, RoutedEventArgs e)
    {
        if (_selected.Count == 0) return;
        _selected.Clear();
        foreach (var v in _visuals.Values) v.SetSelected(false, animate: true);
        Commit();
    }

    private void Commit()
    {
        _updating = true;
        try { SetCurrentValue(SelectedTeethProperty, _selected.OrderBy(n => n).ToArray()); }
        finally { _updating = false; }
        RefreshSummary();
    }

    private void ApplyReadOnly()
    {
        ClearButton.Visibility = IsReadOnly ? Visibility.Collapsed : Visibility.Visible;
        foreach (var v in _visuals.Values) v.Cursor = IsReadOnly ? Cursors.Arrow : Cursors.Hand;
    }

    private void ShowDentition(Dentition d)
    {
        var show = d == Dentition.Permanent ? PermanentCanvas : PrimaryCanvas;
        var hide = d == Dentition.Permanent ? PrimaryCanvas : PermanentCanvas;
        hide.Visibility = Visibility.Collapsed;
        show.Visibility = Visibility.Visible;
        MotionService.FadeIn(show, MotionTokens.Small);
        MotionService.AnimateOpacity(DetailCard, 0);
    }

    // ---------------- summary + detail ----------------

    private void OnLanguageChanged(object? s, System.ComponentModel.PropertyChangedEventArgs e) => RefreshSummary();

    private void RefreshSummary()
    {
        var loc = LocalizationService.Instance;
        SummaryLabel.Text = _selected.Count == 0
            ? (IsReadOnly ? "" : loc["Chart_NoSelection"])
            : string.Format(loc["Chart_SelectedCount"], _selected.Count);
        Chips.ItemsSource = _selected.OrderBy(n => n).Select(n => n.ToString()).ToList();
    }

    private void OnToothHover(int number, bool hovering)
    {
        if (!hovering) { MotionService.AnimateOpacity(DetailCard, 0); return; }

        var loc = LocalizationService.Instance;
        var t = Fdi.Get(number);
        DetailNumber.Text = number.ToString();
        DetailName.Text = loc["Tooth_" + t.NameKey];
        DetailMeta.Text = $"{loc["Quad_" + t.Quadrant]} · {loc["ToothType_" + t.Type]}";
        MotionService.AnimateOpacity(DetailCard, 1);
    }

    // ---------------- one tooth ----------------

    private sealed class ToothVisual : Grid
    {
        private static readonly Dictionary<ToothType, Geometry> Shapes = new()
        {
            [ToothType.Incisor] = Make("M 50,0 C 58,0 66,25 68,45 C 82,50 86,75 82,92 C 80,98 76,100 70,100 L 30,100 C 24,100 20,98 18,92 C 14,75 18,50 32,45 C 34,25 42,0 50,0 Z"),
            [ToothType.Canine] = Make("M 50,0 C 58,0 66,25 68,45 C 84,52 88,72 76,88 L 50,100 L 24,88 C 12,72 16,52 32,45 C 34,25 42,0 50,0 Z"),
            [ToothType.Premolar] = Make("M 50,0 C 58,0 64,25 66,44 C 84,48 90,70 84,90 C 80,100 70,96 62,92 L 50,98 L 38,92 C 30,96 20,100 16,90 C 10,70 16,48 34,44 C 36,25 42,0 50,0 Z"),
            [ToothType.Molar] = Make("M 28,0 C 36,0 42,20 44,40 L 50,34 L 56,40 C 58,20 64,0 72,0 C 78,0 80,24 78,44 C 94,50 98,74 92,90 C 88,100 76,96 66,92 L 50,98 L 34,92 C 24,96 12,100 8,90 C 2,74 6,50 22,44 C 20,24 22,0 28,0 Z"),
        };

        private static Geometry Make(string data) { var g = Geometry.Parse(data); g.Freeze(); return g; }

        private readonly Path _overlay;
        private readonly TextBlock _label;
        private readonly ScaleTransform _scale = new(1, 1);
        private bool _hover;
        private bool _selected;

        public int Number { get; }
        public event Action<int>? Clicked;
        public event Action<int, bool>? HoverChanged;

        public ToothVisual(ToothInfo info, double width, double height, double rotation, TextBlock label)
        {
            Number = info.Number;
            _label = label;
            Width = width; Height = height;
            Background = Brushes.Transparent; // whole box is hit-testable
            Cursor = Cursors.Hand;
            RenderTransformOrigin = new Point(0.5, 0.5);
            var group = new TransformGroup();
            group.Children.Add(_scale);
            group.Children.Add(new RotateTransform(rotation));
            RenderTransform = group;

            var geometry = Shapes[info.Type];
            var basePath = new Path { Data = geometry, Stretch = Stretch.Fill, StrokeThickness = 1.6, StrokeLineJoin = PenLineJoin.Round };
            basePath.SetResourceReference(Shape.FillProperty, "ToothFillBrush");
            basePath.SetResourceReference(Shape.StrokeProperty, "ToothStrokeBrush");

            _overlay = new Path
            {
                Data = geometry, Stretch = Stretch.Fill, StrokeThickness = 1.6, StrokeLineJoin = PenLineJoin.Round,
                Opacity = 0, IsHitTestVisible = false,
                Stroke = new SolidColorBrush(Color.FromRgb(0xA8, 0x84, 0x3D))
            };
            _overlay.SetResourceReference(Shape.FillProperty, "GoldBrush");

            Children.Add(basePath);
            Children.Add(_overlay);

            MouseEnter += (_, _) => { _hover = true; AnimateScale(1.08, 120); HoverChanged?.Invoke(Number, true); };
            MouseLeave += (_, _) => { _hover = false; AnimateScale(1.0, 140); HoverChanged?.Invoke(Number, false); };
            MouseLeftButtonUp += (_, _) => Clicked?.Invoke(Number);
        }

        public void SetSelected(bool selected, bool animate)
        {
            var changed = _selected != selected;
            _selected = selected;

            if (animate) MotionService.AnimateOpacity(_overlay, selected ? 1 : 0, MotionTokens.Small);
            else { _overlay.BeginAnimation(OpacityProperty, null); _overlay.Opacity = selected ? 1 : 0; }

            if (selected) _label.SetResourceReference(TextBlock.ForegroundProperty, "GoldBrush");
            else _label.SetResourceReference(TextBlock.ForegroundProperty, "TextMutedBrush");
            _label.FontWeight = selected ? FontWeights.Bold : FontWeights.Normal;

            if (animate && changed && selected) AnimateScale(1.2, 110, () => AnimateScale(_hover ? 1.08 : 1.0, 160)); // small "pop"
        }

        private void AnimateScale(double to, int ms, Action? done = null)
        {
            if (!MotionService.IsEnabled)
            {
                _scale.BeginAnimation(ScaleTransform.ScaleXProperty, null);
                _scale.BeginAnimation(ScaleTransform.ScaleYProperty, null);
                _scale.ScaleX = _scale.ScaleY = to;
                done?.Invoke();
                return;
            }
            var a = new DoubleAnimation(_scale.ScaleX, to, new Duration(TimeSpan.FromMilliseconds(ms))) { EasingFunction = MotionTokens.EaseOut };
            if (done is not null) a.Completed += (_, _) => done();
            _scale.BeginAnimation(ScaleTransform.ScaleXProperty, a);
            _scale.BeginAnimation(ScaleTransform.ScaleYProperty, a);
        }
    }
}
