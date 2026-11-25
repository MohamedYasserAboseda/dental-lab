using DentalLab.Desktop.ViewModels;

namespace DentalLab.Desktop.Views;

public partial class DoctorPricesWindow : DialogWindowBase
{
    public DoctorPricesWindow(DoctorPricesViewModel viewModel) : base(viewModel) => InitializeComponent();
}
