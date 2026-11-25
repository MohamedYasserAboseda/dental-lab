using DentalLab.Desktop.ViewModels;

namespace DentalLab.Desktop.Views;

public partial class CaseEditorWindow : DialogWindowBase
{
    public CaseEditorWindow(CaseEditorViewModel viewModel) : base(viewModel) => InitializeComponent();
}
