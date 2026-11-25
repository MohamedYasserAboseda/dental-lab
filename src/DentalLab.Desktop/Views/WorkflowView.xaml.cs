using System.Windows.Controls;
using DentalLab.Desktop.Motion;
using DentalLab.Desktop.ViewModels;

namespace DentalLab.Desktop.Views;

public partial class WorkflowView : UserControl
{
    private bool _initialized;

    public WorkflowView()
    {
        InitializeComponent();
        Loaded += async (_, _) =>
        {
            if (_initialized || DataContext is not WorkflowViewModel vm) return;
            _initialized = true;
            vm.Reloaded += () => Dispatcher.BeginInvoke(() => MotionService.FadeIn(BoardHost), System.Windows.Threading.DispatcherPriority.Loaded);
            await vm.InitializeAsync();
        };
    }
}
