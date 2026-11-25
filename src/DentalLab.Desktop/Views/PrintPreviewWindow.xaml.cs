using System.Windows;
using DentalLab.Desktop.ViewModels;

namespace DentalLab.Desktop.Views;

public partial class PrintPreviewWindow : Window
{
    public PrintPreviewWindow(PrintPreviewViewModel viewModel)
    {
        InitializeComponent();
        DataContext = viewModel;
        Loaded += async (_, _) => await viewModel.LoadAsync();
    }
}
