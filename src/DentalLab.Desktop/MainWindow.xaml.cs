using System.ComponentModel;
using System.Windows;
using DentalLab.Desktop.Motion;
using DentalLab.Desktop.ViewModels;

namespace DentalLab.Desktop;

public partial class MainWindow : Window
{
    private const double ExpandedWidth = 264;
    private const double CollapsedWidth = 72;

    public MainWindow(MainViewModel viewModel)
    {
        InitializeComponent();
        DataContext = viewModel;
        viewModel.PropertyChanged += OnViewModelChanged;
        Loaded += (_, _) => MotionService.FadeIn(this, MotionTokens.Page);
    }

    private void OnViewModelChanged(object? sender, PropertyChangedEventArgs e)
    {
        var vm = (MainViewModel)sender!;
        switch (e.PropertyName)
        {
            case nameof(MainViewModel.CurrentPage):
                // Deferred so the new content exists before we animate it.
                Dispatcher.BeginInvoke(() => MotionService.SlideFadeIn(PageHost), System.Windows.Threading.DispatcherPriority.Loaded);
                break;
            case nameof(MainViewModel.IsSidebarExpanded):
                MotionService.AnimateWidth(Sidebar, vm.IsSidebarExpanded ? ExpandedWidth : CollapsedWidth);
                break;
        }
    }
}
