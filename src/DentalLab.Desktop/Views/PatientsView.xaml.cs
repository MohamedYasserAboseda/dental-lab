using System.Windows.Controls;
using DentalLab.Desktop.ViewModels;

namespace DentalLab.Desktop.Views;

public partial class PatientsView : UserControl
{
    private bool _initialized;

    public PatientsView()
    {
        InitializeComponent();
        Loaded += async (_, _) =>
        {
            if (_initialized || DataContext is not PatientsViewModel vm) return;
            _initialized = true;
            await vm.InitializeAsync();
        };
    }
}
