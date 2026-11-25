using DentalLab.Desktop.ViewModels;

namespace DentalLab.Desktop.Views;

public partial class InvoiceEditorWindow : DialogWindowBase
{
    public InvoiceEditorWindow(InvoiceEditorViewModel viewModel) : base(viewModel) => InitializeComponent();
}
