using System.ComponentModel;
using System.Windows;
using System.Windows.Controls;
using DentalLab.Desktop.Motion;
using DentalLab.Desktop.ViewModels;

namespace DentalLab.Desktop.Views;

public partial class DoctorsView : UserControl
{
    private bool _initialized;

    public DoctorsView()
    {
        InitializeComponent();
        Loaded += async (_, _) =>
        {
            if (_initialized || DataContext is not DoctorsViewModel vm) return;
            _initialized = true;
            vm.PropertyChanged += OnViewModelChanged;
            await vm.InitializeAsync();
        };
    }

    private void OnViewModelChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName != nameof(DoctorsViewModel.IsProfileOpen)) return;
        var open = ((DoctorsViewModel)sender!).IsProfileOpen;
        Dispatcher.BeginInvoke(() =>
        {
            if (open) MotionService.SlideFadeIn(ProfileHost);
            else MotionService.FadeIn(ListPanel);
        }, System.Windows.Threading.DispatcherPriority.Loaded);
    }
}
