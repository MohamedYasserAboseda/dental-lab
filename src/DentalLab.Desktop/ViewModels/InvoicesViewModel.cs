using System.Collections.ObjectModel;
using System.Globalization;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using DentalLab.Application.Abstractions;
using DentalLab.Application.Billing;
using DentalLab.Application.Documents;

using DentalLab.Desktop.Services;
using DentalLab.Domain.Entities;
using DentalLab.Shared;
using FluentValidation;
using Microsoft.Extensions.Logging;

namespace DentalLab.Desktop.ViewModels;

public sealed record InvoiceStatusOption(InvoiceStatus? Value, string Text);

public sealed class InvoiceRow(InvoiceListItem item)
{
    private static readonly CultureInfo Inv = CultureInfo.InvariantCulture;
    public int Id => item.Id;
    public string Number => item.Number;
    public string DoctorName => item.DoctorName;
    public string DateText => item.DateUtc.ToLocalTime().ToString("yyyy-MM-dd", Inv);
    public string TotalText => item.Total.ToString("N2", Inv);
    public string PaidText => item.Paid.ToString("N2", Inv);
    public string RemainingText => item.Remaining.ToString("N2", Inv);
    public InvoiceStatus Status => item.Status;
}

public sealed class InvoiceLineRow(InvoiceLineItem l)
{
    private static readonly CultureInfo Inv = CultureInfo.InvariantCulture;
    public string CaseNumber => l.CaseNumber;
    public string QuantityText => l.Quantity.ToString(Inv);
    public string UnitPriceText => l.UnitPrice.ToString("N2", Inv);
    public string DiscountText => l.Discount == 0 ? "" : l.Discount.ToString("N2", Inv);
    public string AmountText => l.Amount.ToString("N2", Inv);
}

public sealed class InvoicePaymentRow(InvoicePaymentItem p)
{
    private static readonly CultureInfo Inv = CultureInfo.InvariantCulture;
    public InvoicePaymentItem Item => p;
    public string ReceiptNumber => p.ReceiptNumber;
    public string DateText => p.Date.ToString("yyyy-MM-dd", Inv);
    public string AmountText => p.Amount.ToString("N2", Inv);
    public PaymentMethod Method => p.Method;
    public bool IsReversed => p.IsReversed;
    public bool CanReverse => !p.IsReversed;
}

public enum PendingInvoiceAction { None, Void, ReversePayment }

public sealed partial class InvoiceDetailsViewModel : ObservableObject
{
    private static readonly CultureInfo Inv = CultureInfo.InvariantCulture;
    private readonly IBillingService _billing;
    private readonly IDoctorService _doctors;
    private readonly IDialogService _dialogs;
    private readonly ILogger _log;
    private readonly Action _close;
    private PendingInvoiceAction _pending;
    private int _pendingPaymentId;

    public LocalizationService Loc => LocalizationService.Instance;
    public ObservableCollection<InvoiceLineRow> Lines { get; } = [];
    public ObservableCollection<InvoicePaymentRow> Payments { get; } = [];

    [ObservableProperty] private InvoiceDetails? _details;
    [ObservableProperty] private bool _isConfirmOpen;
    [ObservableProperty] private string _reasonText = string.Empty;
    [ObservableProperty] private string _confirmTitle = string.Empty;
    [ObservableProperty] private string _errorText = string.Empty;

    public string BackGlyph => Loc.Language == AppLanguage.Arabic ? "\uE72A" : "\uE72B";
    public bool IsVoided => Details?.Status == InvoiceStatus.Voided;
    public bool CanPay => Details is { Status: InvoiceStatus.Unpaid or InvoiceStatus.PartiallyPaid };
    public bool CanVoid => Details is { Status: not InvoiceStatus.Voided, Paid: 0 };
    public string DateText => Details?.DateUtc.ToLocalTime().ToString("yyyy-MM-dd HH:mm", Inv) ?? "";
    public string SubtotalText => Details?.Subtotal.ToString("N2", Inv) ?? "";
    public string DiscountText => Details?.Discount.ToString("N2", Inv) ?? "";
    public string TotalText => Details?.Total.ToString("N2", Inv) ?? "";
    public string PaidText => Details?.Paid.ToString("N2", Inv) ?? "";
    public string RemainingText => Details?.Remaining.ToString("N2", Inv) ?? "";
    public string DescriptionText => string.IsNullOrWhiteSpace(Details?.Description) ? "—" : Details!.Description!;

    public InvoiceDetailsViewModel(IBillingService billing, IDoctorService doctors, IDialogService dialogs, ILogger log, Action close)
    {
        _billing = billing; _doctors = doctors; _dialogs = dialogs; _log = log; _close = close;
        Loc.PropertyChanged += (_, _) => OnPropertyChanged(nameof(BackGlyph));
    }

    public async Task LoadAsync(int id)
    {
        try
        {
            Details = await _billing.GetInvoiceAsync(id);
            Lines.Clear(); Payments.Clear();
            if (Details is null) return;
            foreach (var l in Details.Lines) Lines.Add(new InvoiceLineRow(l));
            foreach (var p in Details.Payments) Payments.Add(new InvoicePaymentRow(p));
        }
        catch (Exception ex) { _log.LogError(ex, "Loading invoice {Id} failed", id); }

        foreach (var n in new[] { nameof(IsVoided), nameof(CanPay), nameof(CanVoid), nameof(DateText), nameof(SubtotalText),
                     nameof(DiscountText), nameof(TotalText), nameof(PaidText), nameof(RemainingText), nameof(DescriptionText) })
            OnPropertyChanged(n);
    }

    [RelayCommand] private void Print() { if (Details is not null) _dialogs.ShowPrintPreview(PrintKind.Invoice, Details.Id); }
    [RelayCommand] private void PrintReceipt(InvoicePaymentRow? row) { if (row is not null) _dialogs.ShowPrintPreview(PrintKind.Receipt, row.Item.Id); }

    [RelayCommand] private void Back() => _close();

    [RelayCommand]
    private async Task RecordPaymentAsync()
    {
        if (Details is null) return;
        var editor = new PaymentEditorViewModel(_billing, _doctors, _log, Details.DoctorId, Details.Id);
        await editor.LoadAsync();
        if (_dialogs.ShowDialog(editor) == true) await LoadAsync(Details.Id);
    }

    [RelayCommand]
    private void StartVoid()
    {
        _pending = PendingInvoiceAction.Void;
        ConfirmTitle = Loc["Invoice_Void"];
        ReasonText = string.Empty; ErrorText = string.Empty; IsConfirmOpen = true;
    }

    [RelayCommand]
    private void StartReverse(InvoicePaymentRow? row)
    {
        if (row is null) return;
        _pending = PendingInvoiceAction.ReversePayment;
        _pendingPaymentId = row.Item.Id;
        ConfirmTitle = $"{Loc["Payments_ReverseTitle"]} {row.ReceiptNumber}";
        ReasonText = string.Empty; ErrorText = string.Empty; IsConfirmOpen = true;
    }

    [RelayCommand] private void CancelConfirm() => IsConfirmOpen = false;

    [RelayCommand]
    private async Task ConfirmAsync()
    {
        if (Details is null) return;
        try
        {
            if (_pending == PendingInvoiceAction.Void) await _billing.VoidInvoiceAsync(Details.Id, ReasonText);
            else if (_pending == PendingInvoiceAction.ReversePayment) await _billing.ReversePaymentAsync(_pendingPaymentId, ReasonText);
            IsConfirmOpen = false;
            await LoadAsync(Details.Id);
        }
        catch (ValidationException ex)
        {
            ErrorText = string.Join("\n", ex.Errors.Select(e => Loc.GetOrNull("Val_" + e.ErrorCode) ?? Loc["Error_Generic"]).Distinct());
        }
        catch (Exception ex) { _log.LogError(ex, "Invoice action failed"); ErrorText = Loc["Error_Generic"]; }
    }
}

public sealed partial class InvoicesViewModel : ObservableObject
{
    private const int PageSize = 12;
    private readonly IBillingService _billing;
    private readonly IDoctorService _doctors;
    private readonly IDialogService _dialogs;
    private readonly ILogger _log;
    private CancellationTokenSource? _debounce;
    private int _loadVersion;
    private bool _building;

    public LocalizationService Loc => LocalizationService.Instance;
    public ObservableCollection<InvoiceRow> Items { get; } = [];
    public ObservableCollection<OptionItem> DoctorOptions { get; } = [];
    public ObservableCollection<InvoiceStatusOption> StatusOptions { get; } = [];

    [ObservableProperty] private string _searchText = string.Empty;
    [ObservableProperty] private int _selectedDoctorId;
    [ObservableProperty] private InvoiceStatusOption? _selectedStatusOption;
    [ObservableProperty] private int _page = 1;
    [ObservableProperty] private int _totalPages = 1;
    [ObservableProperty] private bool _isLoading;
    [ObservableProperty] private InvoiceRow? _selectedInvoice;
    [ObservableProperty] private InvoiceDetailsViewModel? _details;

    public bool IsDetailsOpen => Details is not null;
    public bool IsEmpty => !IsLoading && Items.Count == 0;
    private bool HasFilter => !string.IsNullOrWhiteSpace(SearchText) || SelectedDoctorId > 0 || SelectedStatusOption?.Value is not null;
    public string EmptyMessage => Loc[HasFilter ? "Invoices_NoResults" : "Invoices_Empty"];
    public string PageText => string.Format(Loc["Page_Of"], Page, TotalPages);
    public string PrevGlyph => Loc.Language == AppLanguage.Arabic ? "\uE76C" : "\uE76B";
    public string NextGlyph => Loc.Language == AppLanguage.Arabic ? "\uE76B" : "\uE76C";

    public InvoicesViewModel(IBillingService billing, IDoctorService doctors, IDialogService dialogs, ILoggerFactory loggers)
    {
        _billing = billing; _doctors = doctors; _dialogs = dialogs; _log = loggers.CreateLogger<InvoicesViewModel>();
        BuildOptions();
        Loc.PropertyChanged += (_, _) =>
        {
            BuildOptions();
            foreach (var n in new[] { nameof(EmptyMessage), nameof(PageText), nameof(PrevGlyph), nameof(NextGlyph) }) OnPropertyChanged(n);
        };
    }

    private void BuildOptions()
    {
        _building = true;
        try
        {
            var current = SelectedStatusOption?.Value;
            StatusOptions.Clear();
            StatusOptions.Add(new InvoiceStatusOption(null, Loc["Filter_AllStatuses"]));
            foreach (var s in Enum.GetValues<InvoiceStatus>()) StatusOptions.Add(new InvoiceStatusOption(s, Loc["InvStatus_" + s]));
            SelectedStatusOption = StatusOptions.First(o => o.Value == current);
        }
        finally { _building = false; }
    }

    public async Task InitializeAsync()
    {
        try
        {
            DoctorOptions.Add(new OptionItem(0, Loc["Filter_AllDoctors"]));
            foreach (var d in await _doctors.LookupAsync(activeOnly: false)) DoctorOptions.Add(new OptionItem(d.Id, d.Name));
        }
        catch (Exception ex) { _log.LogError(ex, "Loading doctor filter failed"); }
        await LoadAsync();
    }

    partial void OnDetailsChanged(InvoiceDetailsViewModel? value) => OnPropertyChanged(nameof(IsDetailsOpen));
    partial void OnIsLoadingChanged(bool value) => OnPropertyChanged(nameof(IsEmpty));
    partial void OnSelectedDoctorIdChanged(int value) => FilterChanged();
    partial void OnSelectedStatusOptionChanged(InvoiceStatusOption? value) => FilterChanged();

    private void FilterChanged() { if (_building) return; Page = 1; _ = LoadAsync(); }

    partial void OnSearchTextChanged(string value)
    {
        _debounce?.Cancel();
        _debounce = new CancellationTokenSource();
        _ = DebouncedAsync(_debounce.Token);
    }

    private async Task DebouncedAsync(CancellationToken ct)
    {
        try { await Task.Delay(250, ct); Page = 1; await LoadAsync(); }
        catch (OperationCanceledException) { }
    }

    private async Task LoadAsync()
    {
        var version = ++_loadVersion;
        IsLoading = true;
        try
        {
            var r = await _billing.SearchInvoicesAsync(new InvoiceQuery(SearchText, SelectedDoctorId > 0 ? SelectedDoctorId : null,
                SelectedStatusOption?.Value, Page, PageSize));
            if (version != _loadVersion) return;
            Items.Clear();
            foreach (var i in r.Items) Items.Add(new InvoiceRow(i));
            TotalPages = Math.Max(1, (int)Math.Ceiling(r.TotalCount / (double)PageSize));
        }
        catch (Exception ex) { _log.LogError(ex, "Loading invoices failed"); }
        finally
        {
            if (version == _loadVersion) IsLoading = false;
            foreach (var n in new[] { nameof(IsEmpty), nameof(EmptyMessage), nameof(PageText) }) OnPropertyChanged(n);
        }
    }

    [RelayCommand]
    private async Task AddAsync()
    {
        var editor = new InvoiceEditorViewModel(_billing, _doctors, _log, SelectedDoctorId > 0 ? SelectedDoctorId : null);
        await editor.LoadAsync();
        if (_dialogs.ShowDialog(editor) == true) { Page = 1; await LoadAsync(); }
    }

    [RelayCommand]
    private async Task OpenDetailsAsync(InvoiceRow? row)
    {
        if (row is null) return;
        var vm = new InvoiceDetailsViewModel(_billing, _doctors, _dialogs, _log, CloseDetails);
        await vm.LoadAsync(row.Id);
        Details = vm;
    }

    private void CloseDetails() { Details = null; _ = LoadAsync(); }

    [RelayCommand] private async Task NextPageAsync() { if (Page < TotalPages) { Page++; await LoadAsync(); } }
    [RelayCommand] private async Task PreviousPageAsync() { if (Page > 1) { Page--; await LoadAsync(); } }
}
