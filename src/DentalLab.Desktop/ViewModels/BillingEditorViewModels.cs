using System.Collections.ObjectModel;
using System.Globalization;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using DentalLab.Application.Abstractions;
using DentalLab.Application.Billing;
using DentalLab.Desktop.Helpers;
using DentalLab.Domain.Entities;
using DentalLab.Shared;
using FluentValidation;
using Microsoft.Extensions.Logging;

namespace DentalLab.Desktop.ViewModels;

public sealed partial class CaseSelectItem : ObservableObject
{
    public int Id { get; init; }
    public string CaseNumber { get; init; } = "";
    public string PatientName { get; init; } = "";
    public string RestorationText { get; init; } = "";
    public string DateText { get; init; } = "";
    public decimal Total { get; init; }
    public string TotalText => Total.ToString("N2", CultureInfo.InvariantCulture);
    [ObservableProperty] private bool _isSelected;
}

public sealed partial class InvoiceEditorViewModel : DialogViewModelBase
{
    private static readonly CultureInfo Inv = CultureInfo.InvariantCulture;
    private readonly IBillingService _billing;
    private readonly IDoctorService _doctors;
    private readonly ILogger _log;
    private readonly int? _presetDoctorId;
    private readonly int? _presetCaseId;
    private bool _loading;

    public override string Title => Loc["Invoice_NewTitle"];
    public ObservableCollection<OptionItem> Doctors { get; } = [];
    public ObservableCollection<CaseSelectItem> Cases { get; } = [];

    [ObservableProperty] private int? _selectedDoctorId;
    [ObservableProperty] private string _fromText = string.Empty;
    [ObservableProperty] private string _toText = string.Empty;
    [ObservableProperty] private string _discountText = "0";
    [ObservableProperty] private string _description = string.Empty;
    [ObservableProperty] private string _errorText = string.Empty;

    public bool HasNoCases => Cases.Count == 0;
    private decimal Subtotal => Cases.Where(c => c.IsSelected).Sum(c => c.Total);
    public string SubtotalText => Subtotal.ToString("N2", Inv);
    public string TotalText
        => NumberParser.TryParseMoney(DiscountText, out var d) ? (Subtotal - d).ToString("N2", Inv) : "—";

    public InvoiceEditorViewModel(IBillingService billing, IDoctorService doctors, ILogger log, int? doctorId = null, int? caseId = null)
    { _billing = billing; _doctors = doctors; _log = log; _presetDoctorId = doctorId; _presetCaseId = caseId; }

    public async Task LoadAsync()
    {
        _loading = true;
        try
        {
            foreach (var d in await _doctors.LookupAsync(activeOnly: false)) Doctors.Add(new OptionItem(d.Id, d.Name));
            if (_presetDoctorId is int id)
            {
                SelectedDoctorId = id;
                await LoadCasesCoreAsync(_presetCaseId);
            }
        }
        catch (Exception ex) { _log.LogError(ex, "Loading invoice editor failed"); ErrorText = Loc["Error_Generic"]; }
        finally { _loading = false; }
    }

    partial void OnSelectedDoctorIdChanged(int? value)
    {
        if (!_loading) _ = LoadCasesCoreAsync(null);
    }

    partial void OnDiscountTextChanged(string value) => OnPropertyChanged(nameof(TotalText));

    private async Task LoadCasesCoreAsync(int? preselectCaseId)
    {
        ErrorText = string.Empty;
        Cases.Clear();
        if (SelectedDoctorId is not int doctor) { Refresh(); return; }

        DateTime? from = null, to = null;
        if (!string.IsNullOrWhiteSpace(FromText))
        {
            if (!NumberParser.TryParseDate(FromText, out var f)) { ErrorText = Loc["Val_DateInvalid"]; return; }
            from = f.ToUniversalTime();
        }
        if (!string.IsNullOrWhiteSpace(ToText))
        {
            if (!NumberParser.TryParseDate(ToText, out var t)) { ErrorText = Loc["Val_DateInvalid"]; return; }
            to = t.AddDays(1).ToUniversalTime();
        }

        try
        {
            var arabic = Loc.Language == AppLanguage.Arabic;
            foreach (var c in await _billing.GetUnbilledCasesAsync(doctor, from, to))
            {
                var item = new CaseSelectItem
                {
                    Id = c.CaseId, CaseNumber = c.CaseNumber, PatientName = c.PatientName, Total = c.Total,
                    RestorationText = arabic && !string.IsNullOrWhiteSpace(c.RestorationNameAr) ? c.RestorationNameAr! : c.RestorationName,
                    DateText = c.ReceivedUtc.ToLocalTime().ToString("yyyy-MM-dd", Inv),
                    IsSelected = preselectCaseId == c.CaseId
                };
                item.PropertyChanged += (_, e) => { if (e.PropertyName == nameof(CaseSelectItem.IsSelected)) Refresh(); };
                Cases.Add(item);
            }
        }
        catch (Exception ex) { _log.LogError(ex, "Loading unbilled cases failed"); ErrorText = Loc["Error_Generic"]; }
        Refresh();
    }

    private void Refresh()
    {
        OnPropertyChanged(nameof(HasNoCases));
        OnPropertyChanged(nameof(SubtotalText));
        OnPropertyChanged(nameof(TotalText));
    }

    [RelayCommand] private Task ReloadCasesAsync() => LoadCasesCoreAsync(null);

    [RelayCommand]
    private void SelectAll()
    {
        var all = Cases.Count > 0 && Cases.All(c => c.IsSelected);
        foreach (var c in Cases) c.IsSelected = !all;
    }

    [RelayCommand]
    private async Task SaveAsync()
    {
        ErrorText = string.Empty;
        if (!NumberParser.TryParseMoney(DiscountText, out var discount)) { ErrorText = Loc["Val_NumberInvalid"]; return; }
        var ids = Cases.Where(c => c.IsSelected).Select(c => c.Id).ToList();
        try
        {
            await _billing.CreateInvoiceAsync(new InvoiceInput(SelectedDoctorId ?? 0, ids, discount, Description));
            RequestClose(true);
        }
        catch (ValidationException ex) { ErrorText = ErrorsToText(ex); }
        catch (Exception ex) { _log.LogError(ex, "Creating invoice failed"); ErrorText = Loc["Error_Generic"]; }
    }

    [RelayCommand] private void Cancel() => RequestClose(false);
}

public sealed partial class PaymentEditorViewModel : DialogViewModelBase
{
    private static readonly CultureInfo Inv = CultureInfo.InvariantCulture;
    private readonly IBillingService _billing;
    private readonly IDoctorService _doctors;
    private readonly ILogger _log;
    private readonly int? _presetDoctorId;
    private readonly int? _presetInvoiceId;
    private readonly Dictionary<int, decimal> _remaining = [];
    private bool _loading;

    public override string Title => Loc["Payment_Title"];
    public ObservableCollection<OptionItem> Doctors { get; } = [];
    public ObservableCollection<OptionItem> Invoices { get; } = [];
    public ObservableCollection<OptionItem> Methods { get; } = [];

    [ObservableProperty] private int? _selectedDoctorId;
    [ObservableProperty] private int _selectedInvoiceId;
    [ObservableProperty] private int _selectedMethodId = (int)PaymentMethod.Cash;
    [ObservableProperty] private string _amountText = string.Empty;
    [ObservableProperty] private string _dateText = DateTime.Today.ToString("yyyy-MM-dd", Inv);
    [ObservableProperty] private string _notes = string.Empty;
    [ObservableProperty] private string _errorText = string.Empty;

    public PaymentEditorViewModel(IBillingService billing, IDoctorService doctors, ILogger log, int? doctorId = null, int? invoiceId = null)
    { _billing = billing; _doctors = doctors; _log = log; _presetDoctorId = doctorId; _presetInvoiceId = invoiceId; }

    public async Task LoadAsync()
    {
        _loading = true;
        try
        {
            foreach (var m in Enum.GetValues<PaymentMethod>()) Methods.Add(new OptionItem((int)m, Loc["Method_" + m]));
            foreach (var d in await _doctors.LookupAsync(activeOnly: false)) Doctors.Add(new OptionItem(d.Id, d.Name));
            if (_presetDoctorId is int id)
            {
                SelectedDoctorId = id;
                await LoadInvoicesAsync(id);
                if (_presetInvoiceId is int inv && _remaining.TryGetValue(inv, out var rem))
                {
                    SelectedInvoiceId = inv;
                    AmountText = rem.ToString("0.00", Inv);
                }
            }
            else Invoices.Add(new OptionItem(0, Loc["Payment_OnAccount"]));
        }
        catch (Exception ex) { _log.LogError(ex, "Loading payment editor failed"); ErrorText = Loc["Error_Generic"]; }
        finally { _loading = false; }
    }

    private async Task LoadInvoicesAsync(int doctorId)
    {
        Invoices.Clear();
        _remaining.Clear();
        Invoices.Add(new OptionItem(0, Loc["Payment_OnAccount"]));
        foreach (var i in await _billing.GetOpenInvoicesAsync(doctorId))
        {
            _remaining[i.Id] = i.Remaining;
            Invoices.Add(new OptionItem(i.Id, $"{i.Number}  ({i.Remaining.ToString("N2", Inv)})"));
        }
    }

    partial void OnSelectedDoctorIdChanged(int? value)
    {
        if (_loading) return;
        _ = ReloadInvoicesAsync(value);
    }

    private async Task ReloadInvoicesAsync(int? doctorId)
    {
        try
        {
            SelectedInvoiceId = 0;
            if (doctorId is int id) await LoadInvoicesAsync(id);
        }
        catch (Exception ex) { _log.LogError(ex, "Loading open invoices failed"); }
    }

    partial void OnSelectedInvoiceIdChanged(int value)
    {
        if (_loading) return;
        if (value > 0 && _remaining.TryGetValue(value, out var rem)) AmountText = rem.ToString("0.00", Inv);
    }

    [RelayCommand]
    private async Task SaveAsync()
    {
        ErrorText = string.Empty;
        if (!NumberParser.TryParseMoney(AmountText, out var amount)) { ErrorText = Loc["Val_AmountInvalid"]; return; }
        if (!NumberParser.TryParseDate(DateText, out var date)) { ErrorText = Loc["Val_DateInvalid"]; return; }

        var input = new PaymentInput(SelectedDoctorId ?? 0, SelectedInvoiceId > 0 ? SelectedInvoiceId : null, date, amount,
            (PaymentMethod)SelectedMethodId, Notes);
        try { await _billing.RecordPaymentAsync(input); RequestClose(true); }
        catch (ValidationException ex) { ErrorText = ErrorsToText(ex); }
        catch (Exception ex) { _log.LogError(ex, "Recording payment failed"); ErrorText = Loc["Error_Generic"]; }
    }

    [RelayCommand] private void Cancel() => RequestClose(false);
}

public sealed partial class AdjustmentEditorViewModel : DialogViewModelBase
{
    private readonly IBillingService _billing;
    private readonly ILogger _log;
    private readonly int _doctorId;
    private readonly string _doctorName;

    public override string Title => $"{Loc["Adjust_Title"]} — {_doctorName}";

    [ObservableProperty] private string _amountText = string.Empty;
    [ObservableProperty] private string _description = string.Empty;
    [ObservableProperty] private string _errorText = string.Empty;

    public AdjustmentEditorViewModel(IBillingService billing, ILogger log, int doctorId, string doctorName)
    { _billing = billing; _log = log; _doctorId = doctorId; _doctorName = doctorName; }

    [RelayCommand]
    private async Task SaveAsync()
    {
        ErrorText = string.Empty;
        if (!NumberParser.TryParseMoney(AmountText, out var amount)) { ErrorText = Loc["Val_AmountInvalid"]; return; }
        try { await _billing.AddAdjustmentAsync(new AdjustmentInput(_doctorId, amount, Description)); RequestClose(true); }
        catch (ValidationException ex) { ErrorText = ErrorsToText(ex); }
        catch (Exception ex) { _log.LogError(ex, "Adding adjustment failed"); ErrorText = Loc["Error_Generic"]; }
    }

    [RelayCommand] private void Cancel() => RequestClose(false);
}
