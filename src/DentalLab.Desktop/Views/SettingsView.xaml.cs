using System.Windows.Controls;
using DentalLab.Desktop.ViewModels;

namespace DentalLab.Desktop.Views;

public partial class SettingsView : UserControl
{
    private bool _initialized;

    public SettingsView()
    {
        InitializeComponent();
        Loaded += async (_, _) =>
        {
            if (_initialized || DataContext is not SettingsViewModel vm) return;
            _initialized = true;
            await vm.InitializeAsync();
        };
    }
}
