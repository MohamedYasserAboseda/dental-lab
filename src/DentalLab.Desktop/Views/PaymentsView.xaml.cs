using System.Windows.Controls;
using DentalLab.Desktop.ViewModels;

namespace DentalLab.Desktop.Views;

public partial class PaymentsView : UserControl
{
    private bool _initialized;

    public PaymentsView()
    {
        InitializeComponent();
        Loaded += async (_, _) =>
        {
            if (_initialized || DataContext is not PaymentsViewModel vm) return;
            _initialized = true;
            await vm.InitializeAsync();
        };
    }
}
