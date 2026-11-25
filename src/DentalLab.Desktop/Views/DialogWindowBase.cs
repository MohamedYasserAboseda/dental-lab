using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using DentalLab.Desktop.Motion;
using DentalLab.Desktop.ViewModels;

namespace DentalLab.Desktop.Views;

/// <summary>Chrome-less rounded dialog (see the DialogWindow style). Derived windows only declare their content.</summary>
public abstract class DialogWindowBase : Window
{
    protected DialogWindowBase(IDialogViewModel viewModel)
    {
        DataContext = viewModel;
        viewModel.CloseRequested += saved => DialogResult = saved;
        WindowStartupLocation = WindowStartupLocation.CenterOwner; // not a dependency property, so it cannot live in the style
        MaxHeight = SystemParameters.WorkArea.Height - 10;
        Loaded += (_, _) =>
        {
            if (GetTemplateChild("PART_Root") is UIElement root) MotionService.FadeIn(root, MotionTokens.Dialog);
            if (GetTemplateChild("PART_Card") is FrameworkElement card)
            {
                var scale = new ScaleTransform(1, 1);
                card.RenderTransformOrigin = new Point(0.5, 0.5);
                card.RenderTransform = scale;
                MotionService.ScaleIn(scale, 0.96, MotionTokens.Dialog);
            }
        };
        InputBindings.Add(new KeyBinding(new CloseCommand(this), Key.Escape, ModifierKeys.None));
    }

    public override void OnApplyTemplate()
    {
        base.OnApplyTemplate();
        if (GetTemplateChild("PART_Title") is UIElement title)
            title.MouseLeftButtonDown += (_, e) => { if (e.ButtonState == MouseButtonState.Pressed) DragMove(); };
    }

    /// <summary>Esc closes the dialog without saving.</summary>
    private sealed class CloseCommand(Window window) : ICommand
    {
        public event EventHandler? CanExecuteChanged { add { } remove { } }
        public bool CanExecute(object? parameter) => true;
        public void Execute(object? parameter) => window.DialogResult = false;
    }
}
