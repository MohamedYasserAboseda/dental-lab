using DentalLab.Desktop.ViewModels;

namespace DentalLab.Desktop.Views;

public partial class PatientHistoryWindow : DialogWindowBase
{
    public PatientHistoryWindow(PatientHistoryViewModel viewModel) : base(viewModel) => InitializeComponent();
}
