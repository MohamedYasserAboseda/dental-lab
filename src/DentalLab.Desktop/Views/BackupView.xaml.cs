using System.Windows.Controls;
using DentalLab.Desktop.ViewModels;

namespace DentalLab.Desktop.Views;

public partial class BackupView : UserControl
{
    private bool _initialized;

    public BackupView()
    {
        InitializeComponent();
        Loaded += async (_, _) =>
        {
            if (_initialized || DataContext is not BackupViewModel vm) return;
            _initialized = true;
            await vm.InitializeAsync();
        };
    }
}
