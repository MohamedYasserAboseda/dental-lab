using DentalLab.Desktop.ViewModels;

namespace DentalLab.Desktop.Views;

public partial class PaymentEditorWindow : DialogWindowBase
{
    public PaymentEditorWindow(PaymentEditorViewModel viewModel) : base(viewModel) => InitializeComponent();
}
