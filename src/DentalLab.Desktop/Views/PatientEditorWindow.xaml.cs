using DentalLab.Desktop.ViewModels;

namespace DentalLab.Desktop.Views;

public partial class PatientEditorWindow : DialogWindowBase
{
    public PatientEditorWindow(PatientEditorViewModel viewModel) : base(viewModel) => InitializeComponent();
}
