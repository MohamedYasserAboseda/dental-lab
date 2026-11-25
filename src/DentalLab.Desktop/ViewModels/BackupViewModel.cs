using System.IO;
using System.Collections.ObjectModel;
using System.Diagnostics;
using System.Globalization;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using DentalLab.Application.Abstractions;
using DentalLab.Application.Backup;
using DentalLab.Desktop.Services;
using DentalLab.Shared;
using Microsoft.Extensions.Logging;

namespace DentalLab.Desktop.ViewModels;

public sealed class BackupRow(BackupInfo info)
{
    public BackupInfo Info => info;
    public string FileName => info.FileName;
    public string DateText => DateTime.SpecifyKind(info.CreatedUtc, DateTimeKind.Utc).ToLocalTime().ToString("yyyy-MM-dd HH:mm", CultureInfo.InvariantCulture);
    public string KindText => LocalizationService.Instance["Backup_Kind_" + info.Kind];
    public string SizeText => info.SizeBytes switch
    {
        >= 1_048_576 => (info.SizeBytes / 1_048_576d).ToString("0.0", CultureInfo.InvariantCulture) + " MB",
        >= 1024 => (info.SizeBytes / 1024d).ToString("0", CultureInfo.InvariantCulture) + " KB",
        _ => info.SizeBytes + " B"
    };
}

public sealed partial class BackupViewModel : ObservableObject
{
    private readonly IBackupService _backup;
    private readonly ILogger _log;
    private BackupRow? _pending;
    private bool _loadingSettings;

    public LocalizationService Loc => LocalizationService.Instance;
    public ObservableCollection<BackupRow> Items { get; } = [];
    public ObservableCollection<OptionItem> IntervalOptions { get; } = [];
    public ObservableCollection<OptionItem> RetentionOptions { get; } = [];

    [ObservableProperty] private bool _autoEnabled = true;
    [ObservableProperty] private int _intervalHours = 24;
    [ObservableProperty] private int _retentionCount = 14;
    [ObservableProperty] private bool _compress = true;
    [ObservableProperty] private bool _isBusy;
    [ObservableProperty] private bool _isConfirmOpen;
    [ObservableProperty] private bool _isRestartNeeded;
    [ObservableProperty] private string _statusText = string.Empty;
    [ObservableProperty] private bool _statusIsError;
    [ObservableProperty] private string _confirmFileName = string.Empty;

    public bool HasNoBackups => Items.Count == 0;
    public bool HasStatus => !string.IsNullOrEmpty(StatusText);

    public BackupViewModel(IBackupService backup, ILoggerFactory loggers)
    {
        _backup = backup; _log = loggers.CreateLogger<BackupViewModel>();
        foreach (var h in new[] { 6, 12, 24, 48, 168 }) IntervalOptions.Add(new OptionItem(h, string.Format(Loc["Backup_Hours"], h)));
        foreach (var c in new[] { 7, 14, 30, 60, 100 }) RetentionOptions.Add(new OptionItem(c, c.ToString(CultureInfo.InvariantCulture)));
    }

    public async Task InitializeAsync()
    {
        _loadingSettings = true;
        try
        {
            var s = await _backup.GetSettingsAsync();
            AutoEnabled = s.AutoEnabled; IntervalHours = s.IntervalHours; RetentionCount = s.RetentionCount; Compress = s.Compress;
        }
        catch (Exception ex) { _log.LogError(ex, "Loading backup settings failed"); }
        finally { _loadingSettings = false; }
        await RefreshAsync();
    }

    partial void OnStatusTextChanged(string value) => OnPropertyChanged(nameof(HasStatus));
    partial void OnAutoEnabledChanged(bool value) => _ = SaveSettingsAsync();
    partial void OnIntervalHoursChanged(int value) => _ = SaveSettingsAsync();
    partial void OnRetentionCountChanged(int value) => _ = SaveSettingsAsync();
    partial void OnCompressChanged(bool value) => _ = SaveSettingsAsync();

    private async Task SaveSettingsAsync()
    {
        if (_loadingSettings) return;
        try { await _backup.SaveSettingsAsync(new BackupSettings(AutoEnabled, IntervalHours, RetentionCount, Compress)); }
        catch (Exception ex) { _log.LogError(ex, "Saving backup settings failed"); }
    }

    private void Say(string text, bool error = false) { StatusText = text; StatusIsError = error; }

    [RelayCommand]
    private async Task RefreshAsync()
    {
        try
        {
            var list = await _backup.GetBackupsAsync();
            Items.Clear();
            foreach (var b in list) Items.Add(new BackupRow(b));
        }
        catch (Exception ex) { _log.LogError(ex, "Listing backups failed"); }
        OnPropertyChanged(nameof(HasNoBackups));
    }

    [RelayCommand]
    private async Task CreateBackupAsync()
    {
        IsBusy = true; Say(Loc["Backup_Working"]);
        try
        {
            var info = await _backup.CreateBackupAsync(BackupKind.Manual);
            Say(string.Format(Loc["Backup_Created"], info.FileName));
            await RefreshAsync();
        }
        catch (Exception ex) { _log.LogError(ex, "Creating a backup failed"); Say(Loc["Backup_Error"], true); }
        finally { IsBusy = false; }
    }

    [RelayCommand]
    private async Task VerifyAsync(BackupRow? row)
    {
        if (row is null) return;
        IsBusy = true; Say(Loc["Backup_Working"]);
        try
        {
            var v = await _backup.VerifyAsync(row.Info.FullPath);
            Say(v.IsValid ? Loc["Backup_VerifyOk"] : Loc.GetOrNull("Backup_Verify_" + v.Code) ?? Loc["Backup_Error"], !v.IsValid);
        }
        catch (Exception ex) { _log.LogError(ex, "Verifying a backup failed"); Say(Loc["Backup_Error"], true); }
        finally { IsBusy = false; }
    }

    [RelayCommand]
    private async Task CheckIntegrityAsync()
    {
        IsBusy = true; Say(Loc["Backup_Working"]);
        try
        {
            var v = await _backup.CheckDatabaseIntegrityAsync();
            Say(Loc[v.IsValid ? "Backup_IntegrityOk" : "Backup_IntegrityFailed"], !v.IsValid);
        }
        catch (Exception ex) { _log.LogError(ex, "Integrity check failed"); Say(Loc["Backup_Error"], true); }
        finally { IsBusy = false; }
    }

    [RelayCommand]
    private void StartRestore(BackupRow? row)
    {
        if (row is null) return;
        _pending = row;
        ConfirmFileName = row.FileName;
        IsConfirmOpen = true;
    }

    [RelayCommand] private void CancelRestore() => IsConfirmOpen = false;

    [RelayCommand]
    private async Task ConfirmRestoreAsync()
    {
        if (_pending is null) return;
        IsBusy = true; Say(Loc["Backup_Working"]);
        try
        {
            await _backup.ScheduleRestoreAsync(_pending.Info.FullPath);
            IsConfirmOpen = false;
            IsRestartNeeded = true;
            Say(Loc["Backup_RestoreScheduled"]);
            await RefreshAsync();
        }
        catch (InvalidOperationException ex) when (ex.Message.StartsWith("Backup is not valid: ", StringComparison.Ordinal))
        {
            IsConfirmOpen = false;
            Say(Loc.GetOrNull("Backup_Verify_" + ex.Message["Backup is not valid: ".Length..]) ?? Loc["Backup_Error"], true);
        }
        catch (Exception ex) { _log.LogError(ex, "Scheduling a restore failed"); IsConfirmOpen = false; Say(Loc["Backup_Error"], true); }
        finally { IsBusy = false; }
    }

    [RelayCommand]
    private void RestartNow()
    {
        var exe = Environment.ProcessPath;
        if (string.IsNullOrEmpty(exe)) return;
        // When started through "dotnet DentalLab.dll" the process is dotnet.exe, so pass the dll back to it.
        var isHost = string.Equals(Path.GetFileNameWithoutExtension(exe), "dotnet", StringComparison.OrdinalIgnoreCase);
        var args = isHost ? $"\"{Environment.GetCommandLineArgs()[0]}\"" : "";
        Process.Start(new ProcessStartInfo(exe, args) { UseShellExecute = true });
        System.Windows.Application.Current.Shutdown();
    }

    [RelayCommand]
    private void OpenFolder()
    {
        Directory.CreateDirectory(AppPaths.Backups);
        Process.Start(new ProcessStartInfo("explorer.exe", $"\"{AppPaths.Backups}\"") { UseShellExecute = true });
    }
}
