using DentalLab.Desktop.ViewModels;

namespace DentalLab.Desktop.Views;

public partial class AdjustmentEditorWindow : DialogWindowBase
{
    public AdjustmentEditorWindow(AdjustmentEditorViewModel viewModel) : base(viewModel) => InitializeComponent();
}
