using System.Windows.Controls;
using DentalLab.Desktop.ViewModels;

namespace DentalLab.Desktop.Views;

public partial class DoctorAccountsView : UserControl
{
    private bool _initialized;

    public DoctorAccountsView()
    {
        InitializeComponent();
        Loaded += async (_, _) =>
        {
            if (_initialized || DataContext is not DoctorAccountsViewModel vm) return;
            _initialized = true;
            await vm.InitializeAsync();
        };
    }
}
