using System.IO;
using System.Diagnostics;
using System.Windows;
using DentalLab.Application;
using DentalLab.Application.Abstractions;
using DentalLab.Desktop.Motion;
using DentalLab.Desktop.Services;
using DentalLab.Desktop.ViewModels;
using DentalLab.Infrastructure;
using DentalLab.Infrastructure.Backup;
using DentalLab.Shared;
using Microsoft.Extensions.DependencyInjection;
using Serilog;

namespace DentalLab.Desktop;

public partial class App : System.Windows.Application
{
    public static IServiceProvider Services { get; private set; } = null!;

    protected override async void OnStartup(StartupEventArgs e)
    {
        base.OnStartup(e);
        DispatcherUnhandledException += (_, args) =>
        {
            Log.Error(args.Exception, "Unhandled UI exception");
            MessageBox.Show(args.Exception.Message + (args.Exception.InnerException is { } inner ? "\n\n" + inner.Message : ""),
                "Dental Lab", MessageBoxButton.OK, MessageBoxImage.Error);
            args.Handled = true;
        };

        SplashWindow? splash = null;
        try
        {
            AppPaths.EnsureCreated();
            ConfigureLogging();
            Log.Information("Starting Dental Lab {Version}", typeof(App).Assembly.GetName().Version);

            splash = new SplashWindow();
            splash.Show();
            var clock = Stopwatch.StartNew();

            // A restore requested in the previous session is applied now, before the database is opened.
            try
            {
                if (BackupService.ApplyPendingRestore(BackupLocations.Default)) Log.Information("A pending restore was applied.");
            }
            catch (Exception ex)
            {
                Log.Error(ex, "Applying the pending restore failed");
                MessageBox.Show("تعذّرت استعادة النسخة الاحتياطية وتم الاحتفاظ بالبيانات الحالية.\nThe backup could not be restored; your current data was kept.",
                    "Dental Lab", MessageBoxButton.OK, MessageBoxImage.Warning);
            }

            Services = BuildServices();
            await Services.GetRequiredService<IDatabaseInitializer>().InitializeAsync();
            await ApplyPersistedSettingsAsync();

            var remaining = 1400 - (int)clock.ElapsedMilliseconds; // splash is never artificially long
            if (remaining > 0 && MotionService.IsEnabled) await Task.Delay(remaining);

            var main = Services.GetRequiredService<MainWindow>();
            MainWindow = main;
            ShutdownMode = ShutdownMode.OnMainWindowClose;
            main.Show();
            await splash.FadeOutAsync();
            Services.GetRequiredService<BackupScheduler>().Start();
        }
        catch (Exception ex)
        {
            Log.Fatal(ex, "Startup failed");
            splash?.Close();
            MessageBox.Show("Startup failed:\n" + ex.Message, "Dental Lab", MessageBoxButton.OK, MessageBoxImage.Error);
            Shutdown(1);
        }
    }

    private static async Task ApplyPersistedSettingsAsync()
    {
        var settings = Services.GetRequiredService<ISettingsService>();
        var loc = Services.GetRequiredService<LocalizationService>();
        var theme = Services.GetRequiredService<ThemeService>();

        var themeValue = await settings.GetAsync(SettingKeys.Theme);
        var langValue = await settings.GetAsync(SettingKeys.Language);
        var reduced = await settings.GetAsync(SettingKeys.ReducedMotion);

        MotionService.ReducedMotion = bool.TryParse(reduced, out var r) && r;
        theme.Apply(Enum.TryParse<AppTheme>(themeValue, out var t) ? t : AppTheme.Light);
        loc.SetLanguage(Enum.TryParse<AppLanguage>(langValue, out var l) ? l : AppLanguage.Arabic); // Arabic default
    }

    private static IServiceProvider BuildServices()
    {
        var sc = new ServiceCollection();
        sc.AddLogging(b => b.AddSerilog(dispose: false));
        sc.AddInfrastructure();
        sc.AddSingleton(LocalizationService.Instance);
        sc.AddSingleton<ThemeService>();
        sc.AddSingleton<FileDialogService>();
        sc.AddSingleton<IDialogService, DialogService>();
        sc.AddTransient<DoctorsViewModel>();
        sc.AddTransient<DashboardViewModel>();
        sc.AddTransient<BackupViewModel>();
        sc.AddSingleton<BackupScheduler>();
        sc.AddTransient<PatientsViewModel>();
        sc.AddTransient<CasesViewModel>();
        sc.AddTransient<WorkflowViewModel>();
        sc.AddTransient<InvoicesViewModel>();
        sc.AddTransient<PaymentsViewModel>();
        sc.AddTransient<DoctorAccountsViewModel>();
        sc.AddTransient<SettingsViewModel>();
        sc.AddSingleton<MainViewModel>();
        sc.AddSingleton<MainWindow>();
        return sc.BuildServiceProvider();
    }

    private static void ConfigureLogging()
    {
        Log.Logger = new LoggerConfiguration()
            .MinimumLevel.Information()
            .WriteTo.File(Path.Combine(AppPaths.Logs, "dentallab-.log"),
                rollingInterval: RollingInterval.Day, retainedFileCountLimit: 30)
            .CreateLogger();
    }

    protected override void OnExit(ExitEventArgs e)
    {
        Log.CloseAndFlush();
        base.OnExit(e);
    }
}
