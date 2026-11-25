using System.Windows;
using System.Windows.Media.Animation;
using DentalLab.Desktop.Motion;

namespace DentalLab.Desktop;

public partial class SplashWindow : Window
{
    public SplashWindow()
    {
        InitializeComponent();
        Loaded += (_, _) =>
        {
            MotionService.FadeIn(Root, MotionTokens.Page);
            MotionService.ScaleIn(LogoScale, 0.85, new Duration(TimeSpan.FromMilliseconds(450)));
            if (MotionService.IsEnabled)
            {
                SpinnerRotate.BeginAnimation(System.Windows.Media.RotateTransform.AngleProperty,
                    new DoubleAnimation(0, 360, new Duration(TimeSpan.FromSeconds(1)))
                    { RepeatBehavior = RepeatBehavior.Forever });
            }
        };
    }

    public Task FadeOutAsync()
    {
        var tcs = new TaskCompletionSource();
        MotionService.FadeOut(Root, () => { Close(); tcs.TrySetResult(); });
        return tcs.Task;
    }
}
