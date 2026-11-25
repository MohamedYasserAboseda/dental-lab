using System.Collections.ObjectModel;
using System.Globalization;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using DentalLab.Application.Abstractions;
using DentalLab.Application.Cases;
using DentalLab.Desktop.Helpers;
using DentalLab.Domain.Entities;
using DentalLab.Shared;
using FluentValidation;
using Microsoft.Extensions.Logging;

namespace DentalLab.Desktop.ViewModels;

public sealed partial class CaseEditorViewModel : DialogViewModelBase
{
    private static readonly CultureInfo Inv = CultureInfo.InvariantCulture;

    private readonly ICaseService _cases;
    private readonly IDoctorService _doctors;
    private readonly IPatientService _patients;
    private readonly ICatalogService _catalog;
    private readonly ILogger _log;
    private readonly CaseDetails? _existing;
    private bool _loading;
    private bool _quantityManual;
    private bool _settingQuantity;

    public bool IsEditMode => _existing is not null;
    public override string Title => IsEditMode ? $"{Loc["Case_EditTitle"]} {_existing!.CaseNumber}" : Loc["Case_NewTitle"];

    public ObservableCollection<OptionItem> Doctors { get; } = [];
    public ObservableCollection<OptionItem> Patients { get; } = [];
    public ObservableCollection<OptionItem> Types { get; } = [];
    public ObservableCollection<OptionItem> Materials { get; } = [];

    [ObservableProperty] private int? _selectedDoctorId;
    [ObservableProperty] private int? _selectedPatientId;
    [ObservableProperty] private int? _selectedTypeId;
    [ObservableProperty] private int _selectedMaterialId;
    [ObservableProperty] private string _shade = string.Empty;
    [ObservableProperty] private string _quantityText = "1";
    [ObservableProperty] private string _unitPriceText = "0.00";
    [ObservableProperty] private string _discountText = "0";
    [ObservableProperty] private string _expectedDateText = string.Empty;
    [ObservableProperty] private string _notes = string.Empty;
    [ObservableProperty] private IReadOnlyList<int> _selectedTeeth = Array.Empty<int>();
    [ObservableProperty] private string _errorText = string.Empty;

    public string TotalText
    {
        get
        {
            if (NumberParser.TryParseInt(QuantityText, out var q) && q >= 1 &&
                NumberParser.TryParseMoney(UnitPriceText, out var p) && NumberParser.TryParseMoney(DiscountText, out var d))
                return LabCase.CalculateTotal(q, p, d).ToString("N2", Inv);
            return "—";
        }
    }

    public CaseEditorViewModel(ICaseService cases, IDoctorService doctors, IPatientService patients,
        ICatalogService catalog, ILogger log, CaseDetails? existing = null)
    {
        _cases = cases; _doctors = doctors; _patients = patients; _catalog = catalog; _log = log; _existing = existing;
    }

    // ---------- loading ----------

    public async Task LoadAsync()
    {
        _loading = true;
        try
        {
            var arabic = Loc.Language == AppLanguage.Arabic;
            foreach (var d in await _doctors.LookupAsync(activeOnly: !IsEditMode)) Doctors.Add(new OptionItem(d.Id, d.Name));
            foreach (var t in await _catalog.GetRestorationTypesAsync(activeOnly: !IsEditMode)) Types.Add(new OptionItem(t.Id, t.Display(arabic)));
            Materials.Add(new OptionItem(0, "—"));
            foreach (var m in await _catalog.GetMaterialsAsync(activeOnly: !IsEditMode)) Materials.Add(new OptionItem(m.Id, m.Display(arabic)));

            if (_existing is { } e)
            {
                _quantityManual = true;
                await LoadPatientsAsync(e.DoctorId);
                SelectedDoctorId = e.DoctorId;
                SelectedPatientId = e.PatientId;
                SelectedTypeId = e.RestorationTypeId;
                SelectedMaterialId = e.MaterialId ?? 0;
                Shade = e.Shade ?? "";
                SetQuantity(e.Quantity);
                UnitPriceText = e.UnitPrice.ToString("0.00", Inv);
                DiscountText = e.Discount.ToString("0.00", Inv);
                ExpectedDateText = e.ExpectedDeliveryDate.ToString("yyyy-MM-dd", Inv);
                Notes = e.Notes ?? "";
                SelectedTeeth = e.Teeth.ToArray();
            }
            else ExpectedDateText = DateTime.Today.AddDays(7).ToString("yyyy-MM-dd", Inv);
        }
        catch (Exception ex) { _log.LogError(ex, "Loading case editor failed"); ErrorText = Loc["Error_Generic"]; }
        finally { _loading = false; }
    }

    private async Task LoadPatientsAsync(int doctorId)
    {
        Patients.Clear();
        foreach (var p in await _patients.LookupByDoctorAsync(doctorId)) Patients.Add(new OptionItem(p.Id, p.Name));
    }

    private void SetQuantity(int q)
    {
        _settingQuantity = true;
        QuantityText = q.ToString(Inv);
        _settingQuantity = false;
    }

    // ---------- reactions ----------

    partial void OnSelectedDoctorIdChanged(int? value)
    {
        if (_loading) return;
        _ = OnDoctorChangedAsync(value);
    }

    private async Task OnDoctorChangedAsync(int? doctorId)
    {
        try
        {
            SelectedPatientId = null;
            Patients.Clear();
            if (doctorId is int id) await LoadPatientsAsync(id);
            await RefreshPriceAsync();
        }
        catch (Exception ex) { _log.LogError(ex, "Doctor change failed"); }
    }

    partial void OnSelectedTypeIdChanged(int? value)
    {
        if (_loading) return;
        _ = RefreshPriceAsync();
    }

    private async Task RefreshPriceAsync()
    {
        if (SelectedDoctorId is not int d || SelectedTypeId is not int t) return;
        try { UnitPriceText = (await _catalog.ResolveUnitPriceAsync(d, t)).ToString("0.00", Inv); }
        catch (Exception ex) { _log.LogError(ex, "Price lookup failed"); }
    }

    partial void OnSelectedTeethChanged(IReadOnlyList<int> value)
    {
        if (!_quantityManual) SetQuantity(Math.Max(1, value.Count));
        OnPropertyChanged(nameof(TotalText));
    }

    partial void OnQuantityTextChanged(string value)
    {
        if (!_settingQuantity) _quantityManual = true;
        OnPropertyChanged(nameof(TotalText));
    }

    partial void OnUnitPriceTextChanged(string value) => OnPropertyChanged(nameof(TotalText));
    partial void OnDiscountTextChanged(string value) => OnPropertyChanged(nameof(TotalText));

    // ---------- commands ----------

    [RelayCommand]
    private void AddDays(string? days)
    {
        if (int.TryParse(days, out var n)) ExpectedDateText = DateTime.Today.AddDays(n).ToString("yyyy-MM-dd", Inv);
    }

    [RelayCommand]
    private async Task SaveAsync()
    {
        ErrorText = string.Empty;

        if (!NumberParser.TryParseInt(QuantityText, out var qty) ||
            !NumberParser.TryParseMoney(UnitPriceText, out var price) ||
            !NumberParser.TryParseMoney(DiscountText, out var discount))
        { ErrorText = Loc["Val_NumberInvalid"]; return; }

        if (!NumberParser.TryParseDate(ExpectedDateText, out var date)) { ErrorText = Loc["Val_DateInvalid"]; return; }

        var input = new CaseInput(SelectedDoctorId ?? 0, SelectedPatientId ?? 0, SelectedTypeId ?? 0,
            SelectedMaterialId > 0 ? SelectedMaterialId : null, Shade, qty, price, discount, date, Notes, SelectedTeeth);
        try
        {
            if (_existing is not null) await _cases.UpdateAsync(_existing.Id, input);
            else await _cases.CreateAsync(input);
            RequestClose(true);
        }
        catch (ValidationException ex) { ErrorText = ErrorsToText(ex); }
        catch (Exception ex) { _log.LogError(ex, "Saving case failed"); ErrorText = Loc["Error_Generic"]; }
    }

    [RelayCommand] private void Cancel() => RequestClose(false);
}
