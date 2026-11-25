using System.ComponentModel;
using System.Windows.Controls;
using DentalLab.Desktop.Motion;
using DentalLab.Desktop.ViewModels;

namespace DentalLab.Desktop.Views;

public partial class InvoicesView : UserControl
{
    private bool _initialized;

    public InvoicesView()
    {
        InitializeComponent();
        Loaded += async (_, _) =>
        {
            if (_initialized || DataContext is not InvoicesViewModel vm) return;
            _initialized = true;
            vm.PropertyChanged += OnViewModelChanged;
            await vm.InitializeAsync();
        };
    }

    private void OnViewModelChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName != nameof(InvoicesViewModel.IsDetailsOpen)) return;
        var open = ((InvoicesViewModel)sender!).IsDetailsOpen;
        Dispatcher.BeginInvoke(() =>
        {
            if (open) MotionService.SlideFadeIn(DetailsHost);
            else MotionService.FadeIn(ListPanel);
        }, System.Windows.Threading.DispatcherPriority.Loaded);
    }
}
