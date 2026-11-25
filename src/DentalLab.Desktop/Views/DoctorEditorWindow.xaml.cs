using System.Windows;
using System.Windows.Input;
using DentalLab.Desktop.Motion;
using DentalLab.Desktop.ViewModels;

namespace DentalLab.Desktop.Views;

public partial class DoctorEditorWindow : Window
{
    public DoctorEditorWindow(DoctorEditorViewModel viewModel)
    {
        InitializeComponent();
        DataContext = viewModel;
        viewModel.CloseRequested += saved => DialogResult = saved;

        TitleText.MouseLeftButtonDown += (_, e) => { if (e.ButtonState == MouseButtonState.Pressed) DragMove(); };
        Loaded += (_, _) =>
        {
            MotionService.FadeIn(Root, MotionTokens.Dialog);
            MotionService.ScaleIn(CardScale, 0.96, MotionTokens.Dialog);
        };
    }
}
