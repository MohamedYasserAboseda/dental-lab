using System.ComponentModel;
using System.Windows.Controls;
using DentalLab.Desktop.Motion;
using DentalLab.Desktop.ViewModels;

namespace DentalLab.Desktop.Views;

public partial class CasesView : UserControl
{
    private bool _initialized;

    public CasesView()
    {
        InitializeComponent();
        Loaded += async (_, _) =>
        {
            if (_initialized || DataContext is not CasesViewModel vm) return;
            _initialized = true;
            vm.PropertyChanged += OnViewModelChanged;
            await vm.InitializeAsync();
        };
    }

    private void OnViewModelChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName != nameof(CasesViewModel.IsDetailsOpen)) return;
        var open = ((CasesViewModel)sender!).IsDetailsOpen;
        Dispatcher.BeginInvoke(() =>
        {
            if (open) MotionService.SlideFadeIn(DetailsHost);
            else MotionService.FadeIn(ListPanel);
        }, System.Windows.Threading.DispatcherPriority.Loaded);
    }
}
