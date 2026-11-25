using System.Windows.Threading;
using DentalLab.Application.Abstractions;
using Microsoft.Extensions.Logging;

namespace DentalLab.Desktop.Services;

/// <summary>Checks every 30 minutes whether an automatic backup is due (the service decides from the settings).</summary>
public sealed class BackupScheduler(IBackupService backup, ILoggerFactory loggers)
{
    private readonly ILogger _log = loggers.CreateLogger<BackupScheduler>();
    private DispatcherTimer? _timer;

    public void Start()
    {
        if (_timer is not null) return;
        _timer = new DispatcherTimer { Interval = TimeSpan.FromMinutes(30) };
        _timer.Tick += async (_, _) => await TickAsync();
        _timer.Start();
        _ = StartupCheckAsync();
    }

    private async Task StartupCheckAsync()
    {
        await Task.Delay(TimeSpan.FromSeconds(20)); // let the app finish starting first
        await TickAsync();
    }

    private async Task TickAsync()
    {
        try
        {
            var info = await backup.RunScheduledBackupAsync();
            if (info is not null) _log.LogInformation("Automatic backup created: {File}", info.FileName);
        }
        catch (Exception ex) { _log.LogError(ex, "Automatic backup failed"); }
    }
}
