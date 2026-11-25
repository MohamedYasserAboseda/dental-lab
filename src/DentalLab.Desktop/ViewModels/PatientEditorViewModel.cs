using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using DentalLab.Application.Abstractions;
using DentalLab.Application.Patients;
using FluentValidation;
using Microsoft.Extensions.Logging;

namespace DentalLab.Desktop.ViewModels;

public sealed partial class PatientEditorViewModel : DialogViewModelBase
{
    private readonly IPatientService _patients;
    private readonly IDoctorService _doctors;
    private readonly ILogger _log;
    private readonly int? _id;

    public bool IsEditMode => _id.HasValue;
    public override string Title => Loc[IsEditMode ? "Patient_EditTitle" : "Patient_AddTitle"];
    public ObservableCollection<OptionItem> Doctors { get; } = [];

    [ObservableProperty] private string _fullName = string.Empty;
    [ObservableProperty] private string _code = string.Empty;
    [ObservableProperty] private string _notes = string.Empty;
    [ObservableProperty] private int? _selectedDoctorId;
    [ObservableProperty] private string _errorText = string.Empty;

    public PatientEditorViewModel(IPatientService patients, IDoctorService doctors, ILogger log,
        PatientDetails? existing = null, int? defaultDoctorId = null)
    {
        _patients = patients; _doctors = doctors; _log = log;
        if (existing is not null)
        {
            _id = existing.Id;
            FullName = existing.FullName; Code = existing.Code ?? ""; Notes = existing.Notes ?? "";
            SelectedDoctorId = existing.DoctorId;
        }
        else SelectedDoctorId = defaultDoctorId;
    }

    public async Task LoadAsync()
    {
        try
        {
            foreach (var d in await _doctors.LookupAsync(activeOnly: !IsEditMode)) Doctors.Add(new OptionItem(d.Id, d.Name));
        }
        catch (Exception ex) { _log.LogError(ex, "Loading doctors failed"); }
    }

    [RelayCommand]
    private async Task SaveAsync()
    {
        ErrorText = string.Empty;
        var input = new PatientInput(FullName, SelectedDoctorId ?? 0, Code, Notes);
        try
        {
            if (_id is int id) await _patients.UpdateAsync(id, input); else await _patients.CreateAsync(input);
            RequestClose(true);
        }
        catch (ValidationException ex) { ErrorText = ErrorsToText(ex); }
        catch (Exception ex) { _log.LogError(ex, "Saving patient failed"); ErrorText = Loc["Error_Generic"]; }
    }

    [RelayCommand] private void Cancel() => RequestClose(false);
}
