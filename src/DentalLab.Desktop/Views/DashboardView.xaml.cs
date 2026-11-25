using System.Windows.Controls;
using DentalLab.Desktop.Motion;
using DentalLab.Desktop.ViewModels;

namespace DentalLab.Desktop.Views;

public partial class DashboardView : UserControl
{
    private bool _initialized;

    public DashboardView()
    {
        InitializeComponent();
        Loaded += async (_, _) =>
        {
            if (_initialized || DataContext is not DashboardViewModel vm) return;
            _initialized = true;
            await vm.InitializeAsync();
        };
    }
}
