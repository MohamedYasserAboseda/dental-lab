using DentalLab.Desktop.ViewModels;

namespace DentalLab.Desktop.Views;

public partial class CatalogItemEditorWindow : DialogWindowBase
{
    public CatalogItemEditorWindow(CatalogItemEditorViewModel viewModel) : base(viewModel) => InitializeComponent();
}
