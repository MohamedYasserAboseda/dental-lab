using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using DentalLab.Application;
using DentalLab.Application.Abstractions;
using DentalLab.Desktop.Services;
using DentalLab.Shared;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using System.Windows.Threading;

namespace DentalLab.Desktop.ViewModels;

public sealed partial class MainViewModel : ObservableObject
{
    private readonly ThemeService _theme;
    private readonly ISettingsService _settings;
    private readonly ILogger<MainViewModel> _log;
    private readonly IServiceProvider _services;

    public LocalizationService Loc => LocalizationService.Instance;
    public IReadOnlyList<NavItemViewModel> NavItems { get; }

    [ObservableProperty] private NavItemViewModel? _selectedNavItem;
    [ObservableProperty] private object? _currentPage;
    [ObservableProperty] private bool _isSidebarExpanded = true;
    [ObservableProperty] private string _currentDateText = string.Empty;

    public string ThemeGlyph => _theme.Current == AppTheme.Light ? "\uE708" : "\uE706"; // moon / sun
    public string VersionText => "v" + typeof(MainViewModel).Assembly.GetName().Version?.ToString(3);

    public MainViewModel(ThemeService theme, ISettingsService settings, ILogger<MainViewModel> log, IServiceProvider services)
    {
        _theme = theme; _settings = settings; _log = log; _services = services;

        // Glyphs are Segoe MDL2 Assets placeholders; final icon set comes with the polish pass.
        NavItems =
        [
            new("Dashboard",      "\uE80F", "Nav_Dashboard"),
            new("Doctors",        "\uE77B", "Nav_Doctors"),
            new("Patients",       "\uE716", "Nav_Patients"),
            new("Cases",          "\uE7C3", "Nav_Cases"),
            new("Workflow",       "\uE895", "Nav_Workflow"),
            new("Invoices",       "\uE8A5", "Nav_Invoices"),
            new("Payments",       "\uE8C7", "Nav_Payments"),
            new("DoctorAccounts", "\uE825", "Nav_DoctorAccounts"),
            new("Inventory",      "\uE7B8", "Nav_Inventory"),
            new("Suppliers",      "\uE719", "Nav_Suppliers"),
            new("Expenses",       "\uE1D0", "Nav_Expenses"),
            new("Reports",        "\uE9D2", "Nav_Reports"),
            new("Settings",       "\uE713", "Nav_Settings"),
            new("Backup",         "\uE896", "Nav_Backup"),
        ];
        SelectedNavItem = NavItems[0];

        var timer = new DispatcherTimer { Interval = TimeSpan.FromSeconds(20) };
        timer.Tick += (_, _) => UpdateClock();
        timer.Start();
        UpdateClock();
    }

    partial void OnSelectedNavItemChanged(NavItemViewModel? value)
    {
        if (value is null) return;
        CurrentPage = value.Key switch
        {
            "Dashboard" => _services.GetRequiredService<DashboardViewModel>(),
            "Backup" => _services.GetRequiredService<BackupViewModel>(),
            "Doctors" => _services.GetRequiredService<DoctorsViewModel>(),
            "Patients" => _services.GetRequiredService<PatientsViewModel>(),
            "Cases" => _services.GetRequiredService<CasesViewModel>(),
            "Workflow" => _services.GetRequiredService<WorkflowViewModel>(),
            "Invoices" => _services.GetRequiredService<InvoicesViewModel>(),
            "Payments" => _services.GetRequiredService<PaymentsViewModel>(),
            "DoctorAccounts" => _services.GetRequiredService<DoctorAccountsViewModel>(),
            "Settings" => _services.GetRequiredService<SettingsViewModel>(),
            _ => new PlaceholderViewModel(value)
        };
    }

    private void UpdateClock()
        => CurrentDateText = DateTime.Now.ToString("dddd d MMMM yyyy  •  hh:mm tt", Loc.Culture);

    [RelayCommand] private void ToggleSidebar() => IsSidebarExpanded = !IsSidebarExpanded;

    [RelayCommand] private void OpenSettings() => SelectedNavItem = NavItems.First(n => n.Key == "Settings");

    [RelayCommand]
    private async Task ToggleThemeAsync()
    {
        var theme = _theme.Toggle();
        OnPropertyChanged(nameof(ThemeGlyph));
        try { await _settings.SetAsync(SettingKeys.Theme, theme.ToString()); }
        catch (Exception ex) { _log.LogError(ex, "Failed to persist theme"); }
    }

    [RelayCommand]
    private async Task ToggleLanguageAsync()
    {
        var next = Loc.Language == AppLanguage.Arabic ? AppLanguage.English : AppLanguage.Arabic;
        Loc.SetLanguage(next);
        UpdateClock();
        try { await _settings.SetAsync(SettingKeys.Language, next.ToString()); }
        catch (Exception ex) { _log.LogError(ex, "Failed to persist language"); }
    }
}
