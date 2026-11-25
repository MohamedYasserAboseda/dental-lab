using System.ComponentModel;
using System.Windows;
using System.Windows.Controls;
using DentalLab.Desktop.Motion;
using DentalLab.Desktop.ViewModels;

namespace DentalLab.Desktop.Views;

public partial class CaseDetailsView : UserControl
{
    public CaseDetailsView()
    {
        InitializeComponent();
        DataContextChanged += (_, e) =>
        {
            if (e.OldValue is CaseDetailsViewModel o) o.PropertyChanged -= OnViewModelChanged;
            if (e.NewValue is CaseDetailsViewModel n) n.PropertyChanged += OnViewModelChanged;
        };
        Loaded += (_, _) => UpdateProgress(animate: false);
        ProgressTrack.SizeChanged += (_, _) => UpdateProgress(animate: false);
    }

    private void OnViewModelChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName == nameof(CaseDetailsViewModel.ProgressFraction)) UpdateProgress(animate: true);
    }

    private void UpdateProgress(bool animate)
    {
        if (DataContext is not CaseDetailsViewModel vm) return;
        var target = ProgressTrack.ActualWidth * vm.ProgressFraction;
        if (animate) MotionService.AnimateWidth(ProgressFill, target, MotionTokens.Page);
        else { ProgressFill.BeginAnimation(WidthProperty, null); ProgressFill.Width = target; }
    }
}
