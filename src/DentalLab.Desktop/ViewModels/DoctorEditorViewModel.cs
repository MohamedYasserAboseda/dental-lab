using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using DentalLab.Application.Abstractions;
using DentalLab.Application.Doctors;
using DentalLab.Desktop.Helpers;
using DentalLab.Desktop.Services;
using FluentValidation;
using Microsoft.Extensions.Logging;

namespace DentalLab.Desktop.ViewModels;

public sealed partial class DoctorEditorViewModel : ObservableObject
{
    private readonly IDoctorService _service;
    private readonly ILogger _log;
    private readonly int? _id;

    public LocalizationService Loc => LocalizationService.Instance;
    public bool IsEditMode => _id.HasValue;
    public bool IsCreateMode => !IsEditMode;
    public string Title => Loc[IsEditMode ? "Editor_EditTitle" : "Editor_AddTitle"];

    [ObservableProperty] private string _fullName = string.Empty;
    [ObservableProperty] private string _clinicName = string.Empty;
    [ObservableProperty] private string _phone = string.Empty;
    [ObservableProperty] private string _email = string.Empty;
    [ObservableProperty] private string _address = string.Empty;
    [ObservableProperty] private string _notes = string.Empty;
    [ObservableProperty] private string _openingBalanceText = "0";
    [ObservableProperty] private bool _isActive = true;
    [ObservableProperty] private string _errorText = string.Empty;

    /// <summary>Raised with true when the doctor was saved, false when cancelled.</summary>
    public event Action<bool>? CloseRequested;

    public DoctorEditorViewModel(IDoctorService service, ILogger log, DoctorDetails? existing = null)
    {
        _service = service; _log = log;
        if (existing is null) return;

        _id = existing.Id;
        FullName = existing.FullName;
        ClinicName = existing.ClinicName ?? "";
        Phone = existing.Phone ?? "";
        Email = existing.Email ?? "";
        Address = existing.Address ?? "";
        Notes = existing.Notes ?? "";
        IsActive = existing.IsActive;
    }

    [RelayCommand]
    private async Task SaveAsync()
    {
        ErrorText = string.Empty;

        if (!NumberParser.TryParseMoney(OpeningBalanceText, out var opening))
        {
            ErrorText = Loc["Val_BalanceInvalid"];
            return;
        }

        var input = new DoctorInput(FullName, ClinicName, Phone, Email, Address, Notes, opening, IsActive);
        try
        {
            if (_id is int id) await _service.UpdateAsync(id, input);
            else await _service.CreateAsync(input);
            CloseRequested?.Invoke(true);
        }
        catch (ValidationException ex)
        {
            ErrorText = string.Join("\n", ex.Errors.Select(e => Loc["Val_" + e.ErrorCode]).Distinct());
        }
        catch (Exception ex)
        {
            _log.LogError(ex, "Saving doctor failed");
            ErrorText = Loc["Error_Generic"];
        }
    }

    [RelayCommand]
    private void Cancel() => CloseRequested?.Invoke(false);
}
