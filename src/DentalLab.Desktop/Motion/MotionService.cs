using System.Windows;
using System.Windows.Media;
using System.Windows.Media.Animation;

namespace DentalLab.Desktop.Motion;

/// <summary>Timing + easing rules for the whole app. Change values here, never inline.</summary>
public static class MotionTokens
{
    public static readonly Duration Hover  = new(TimeSpan.FromMilliseconds(150));
    public static readonly Duration Button = new(TimeSpan.FromMilliseconds(130));
    public static readonly Duration Small  = new(TimeSpan.FromMilliseconds(220));
    public static readonly Duration Page   = new(TimeSpan.FromMilliseconds(300));
    public static readonly Duration Dialog = new(TimeSpan.FromMilliseconds(220));

    public static readonly IEasingFunction EaseOut = Frozen(new CubicEase { EasingMode = EasingMode.EaseOut });
    public static readonly IEasingFunction EaseInOut = Frozen(new CubicEase { EasingMode = EasingMode.EaseInOut });

    private static T Frozen<T>(T f) where T : Freezable { f.Freeze(); return f; }
}

/// <summary>
/// Reusable animation helpers. All of them degrade to instant state changes when
/// reduced motion is on (app setting) or Windows animations are disabled.
/// Only Opacity / Transform / Width are animated.
/// </summary>
public static class MotionService
{
    public static bool ReducedMotion { get; set; }
    public static bool IsEnabled => !ReducedMotion && SystemParameters.ClientAreaAnimation;

    public static void FadeIn(UIElement el, Duration? duration = null)
    {
        if (!IsEnabled) { el.Opacity = 1; return; }
        var a = new DoubleAnimation(0, 1, duration ?? MotionTokens.Small) { EasingFunction = MotionTokens.EaseOut };
        a.Completed += (_, _) => { el.BeginAnimation(UIElement.OpacityProperty, null); el.Opacity = 1; };
        el.BeginAnimation(UIElement.OpacityProperty, a);
    }

    public static void FadeOut(UIElement el, Action? completed = null, Duration? duration = null)
    {
        if (!IsEnabled) { el.Opacity = 0; completed?.Invoke(); return; }
        var a = new DoubleAnimation(el.Opacity, 0, duration ?? MotionTokens.Small) { EasingFunction = MotionTokens.EaseOut };
        a.Completed += (_, _) => { el.BeginAnimation(UIElement.OpacityProperty, null); el.Opacity = 0; completed?.Invoke(); };
        el.BeginAnimation(UIElement.OpacityProperty, a);
    }

    /// <summary>Fade + small vertical slide. Direction-agnostic, so it behaves the same in RTL and LTR.</summary>
    public static void SlideFadeIn(FrameworkElement el, double fromY = 14, Duration? duration = null)
    {
        var d = duration ?? MotionTokens.Page;
        if (!IsEnabled) { el.Opacity = 1; el.RenderTransform = Transform.Identity; return; }

        var tt = new TranslateTransform(0, fromY);
        el.RenderTransform = tt;

        var slide = new DoubleAnimation(fromY, 0, d) { EasingFunction = MotionTokens.EaseOut };
        slide.Completed += (_, _) => { tt.BeginAnimation(TranslateTransform.YProperty, null); tt.Y = 0; };
        tt.BeginAnimation(TranslateTransform.YProperty, slide);

        FadeIn(el, d);
    }

    public static void ScaleIn(ScaleTransform scale, double from = 0.85, Duration? duration = null)
    {
        if (!IsEnabled) { scale.ScaleX = scale.ScaleY = 1; return; }
        var a = new DoubleAnimation(from, 1, duration ?? MotionTokens.Page) { EasingFunction = MotionTokens.EaseOut };
        scale.BeginAnimation(ScaleTransform.ScaleXProperty, a);
        scale.BeginAnimation(ScaleTransform.ScaleYProperty, a);
    }

    /// <summary>Hold-end opacity animation; always call through here so the held value stays consistent.</summary>
    public static void AnimateOpacity(UIElement el, double to, Duration? duration = null)
    {
        if (!IsEnabled) { el.BeginAnimation(UIElement.OpacityProperty, null); el.Opacity = to; return; }
        var a = new DoubleAnimation(el.Opacity, to, duration ?? MotionTokens.Hover) { EasingFunction = MotionTokens.EaseOut };
        el.BeginAnimation(UIElement.OpacityProperty, a);
    }

    public static void AnimateWidth(FrameworkElement el, double to, Duration? duration = null)
    {
        if (!IsEnabled) { el.Width = to; return; }
        var from = double.IsNaN(el.Width) ? el.ActualWidth : el.Width;
        var a = new DoubleAnimation(from, to, duration ?? MotionTokens.Small) { EasingFunction = MotionTokens.EaseInOut };
        a.Completed += (_, _) => { el.BeginAnimation(FrameworkElement.WidthProperty, null); el.Width = to; };
        el.BeginAnimation(FrameworkElement.WidthProperty, a);
    }
}
