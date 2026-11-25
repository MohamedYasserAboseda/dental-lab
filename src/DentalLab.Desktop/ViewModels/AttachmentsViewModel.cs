using System.IO;
using System.Collections.ObjectModel;
using System.Diagnostics;
using System.Globalization;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using DentalLab.Application.Abstractions;
using DentalLab.Application.Attachments;
using DentalLab.Desktop.Services;
using FluentValidation;
using Microsoft.Extensions.Logging;

namespace DentalLab.Desktop.ViewModels;

public sealed class AttachmentRow(AttachmentItem item)
{
    public AttachmentItem Item => item;
    public int Id => item.Id;
    public string FileName => item.FileName;
    public string KindText => LocalizationService.Instance["Attach_Kind_" + item.Kind];
    public string DateText => DateTime.SpecifyKind(item.AddedUtc, DateTimeKind.Utc).ToLocalTime().ToString("yyyy-MM-dd HH:mm", CultureInfo.InvariantCulture);
    public string SizeText => item.SizeBytes switch
    {
        >= 1_048_576 => (item.SizeBytes / 1_048_576d).ToString("0.0", CultureInfo.InvariantCulture) + " MB",
        >= 1024 => (item.SizeBytes / 1024d).ToString("0", CultureInfo.InvariantCulture) + " KB",
        _ => item.SizeBytes + " B"
    };
}

public sealed partial class AttachmentsViewModel : ObservableObject
{
    private readonly IAttachmentService _service;
    private readonly FileDialogService _files;
    private readonly ILogger _log;
    private int _caseId;
    private AttachmentRow? _pending;

    public LocalizationService Loc => LocalizationService.Instance;
    public ObservableCollection<AttachmentRow> Items { get; } = [];

    [ObservableProperty] private bool _isConfirmOpen;
    [ObservableProperty] private string _statusText = string.Empty;
    [ObservableProperty] private bool _statusIsError;

    public bool HasNoItems => Items.Count == 0;
    public bool HasStatus => !string.IsNullOrEmpty(StatusText);

    public AttachmentsViewModel(IAttachmentService service, FileDialogService files, ILogger log)
    { _service = service; _files = files; _log = log; }

    partial void OnStatusTextChanged(string value) => OnPropertyChanged(nameof(HasStatus));

    public async Task LoadAsync(int caseId)
    {
        _caseId = caseId;
        try
        {
            var list = await _service.ListAsync(caseId);
            Items.Clear();
            foreach (var a in list) Items.Add(new AttachmentRow(a));
        }
        catch (Exception ex) { _log.LogError(ex, "Loading attachments failed"); }
        OnPropertyChanged(nameof(HasNoItems));
    }

    [RelayCommand]
    private async Task AddAsync()
    {
        var picked = _files.PickAttachments();
        if (picked.Length == 0) return;

        var added = 0;
        var problems = new List<string>();
        foreach (var path in picked)
        {
            try { await _service.AddAsync(_caseId, path); added++; }
            catch (ValidationException ex)
            {
                var why = string.Join(" ", ex.Errors.Select(e => Loc.GetOrNull("Val_" + e.ErrorCode) ?? Loc["Error_Generic"]).Distinct());
                problems.Add($"{Path.GetFileName(path)}: {why}");
            }
            catch (Exception ex)
            {
                _log.LogError(ex, "Adding an attachment failed");
                problems.Add($"{Path.GetFileName(path)}: {Loc["Error_Generic"]}");
            }
        }

        var parts = new List<string>();
        if (added > 0) parts.Add(string.Format(Loc["Attach_Added"], added));
        parts.AddRange(problems);
        StatusText = string.Join("\n", parts);
        StatusIsError = problems.Count > 0;
        await LoadAsync(_caseId);
    }

    [RelayCommand]
    private async Task OpenAsync(AttachmentRow? row)
    {
        if (row is null) return;
        try
        {
            var path = await _service.GetFilePathAsync(row.Id);
            if (path is null) { StatusText = Loc["Attach_OpenError"]; StatusIsError = true; return; }
            Process.Start(new ProcessStartInfo(path) { UseShellExecute = true });
        }
        catch (Exception ex)
        {
            _log.LogError(ex, "Opening an attachment failed");
            StatusText = Loc["Attach_OpenError"]; StatusIsError = true;
        }
    }

    [RelayCommand]
    private void StartRemove(AttachmentRow? row)
    {
        if (row is null) return;
        _pending = row;
        IsConfirmOpen = true;
    }

    [RelayCommand] private void CancelRemove() => IsConfirmOpen = false;

    [RelayCommand]
    private async Task ConfirmRemoveAsync()
    {
        if (_pending is null) return;
        try
        {
            await _service.RemoveAsync(_pending.Id);
            IsConfirmOpen = false;
            StatusText = string.Empty;
            await LoadAsync(_caseId);
        }
        catch (Exception ex)
        {
            _log.LogError(ex, "Removing an attachment failed");
            IsConfirmOpen = false; StatusText = Loc["Error_Generic"]; StatusIsError = true;
        }
    }
}
