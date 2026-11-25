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

public sealed class PaymentRow(PaymentListItem item)
{
    private static readonly CultureInfo Inv = CultureInfo.InvariantCulture;
    public int Id => item.Id;
    public string ReceiptNumber => item.ReceiptNumber;
    public string DoctorName => item.DoctorName;
    public string DateText => item.Date.ToString("yyyy-MM-dd", Inv);
    public string AmountText => item.Amount.ToString("N2", Inv);
    public PaymentMethod Method => item.Method;
    public string InvoiceText => item.InvoiceNumber ?? "—";
    public bool IsReversed => item.IsReversed;
    public bool CanReverse => !item.IsReversed;
}

public sealed partial class PaymentsViewModel : ObservableObject
{
    private const int PageSize = 12;
    private readonly IBillingService _billing;
    private readonly IDoctorService _doctors;
    private readonly IDialogService _dialogs;
    private readonly ILogger _log;
    private CancellationTokenSource? _debounce;
    private int _loadVersion;
    private PaymentRow? _pendingRow;

    public LocalizationService Loc => LocalizationService.Instance;
    public ObservableCollection<PaymentRow> Items { get; } = [];
    public ObservableCollection<OptionItem> DoctorOptions { get; } = [];

    [ObservableProperty] private string _searchText = string.Empty;
    [ObservableProperty] private int _selectedDoctorId;
    [ObservableProperty] private int _page = 1;
    [ObservableProperty] private int _totalPages = 1;
    [ObservableProperty] private bool _isLoading;
    [ObservableProperty] private bool _isConfirmOpen;
    [ObservableProperty] private string _confirmTitle = string.Empty;
    [ObservableProperty] private string _reasonText = string.Empty;
    [ObservableProperty] private string _errorText = string.Empty;

    public bool IsEmpty => !IsLoading && Items.Count == 0;
    public string EmptyMessage => Loc[!string.IsNullOrWhiteSpace(SearchText) || SelectedDoctorId > 0 ? "Payments_NoResults" : "Payments_Empty"];
    public string PageText => string.Format(Loc["Page_Of"], Page, TotalPages);
    public string PrevGlyph => Loc.Language == AppLanguage.Arabic ? "\uE76C" : "\uE76B";
    public string NextGlyph => Loc.Language == AppLanguage.Arabic ? "\uE76B" : "\uE76C";

    public PaymentsViewModel(IBillingService billing, IDoctorService doctors, IDialogService dialogs, ILoggerFactory loggers)
    {
        _billing = billing; _doctors = doctors; _dialogs = dialogs; _log = loggers.CreateLogger<PaymentsViewModel>();
        Loc.PropertyChanged += (_, _) =>
        {
            foreach (var n in new[] { nameof(EmptyMessage), nameof(PageText), nameof(PrevGlyph), nameof(NextGlyph) }) OnPropertyChanged(n);
        };
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

    partial void OnIsLoadingChanged(bool value) => OnPropertyChanged(nameof(IsEmpty));
    partial void OnSelectedDoctorIdChanged(int value) { Page = 1; _ = LoadAsync(); }

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
            var r = await _billing.SearchPaymentsAsync(new PaymentQuery(SearchText, SelectedDoctorId > 0 ? SelectedDoctorId : null, Page, PageSize));
            if (version != _loadVersion) return;
            Items.Clear();
            foreach (var p in r.Items) Items.Add(new PaymentRow(p));
            TotalPages = Math.Max(1, (int)Math.Ceiling(r.TotalCount / (double)PageSize));
        }
        catch (Exception ex) { _log.LogError(ex, "Loading payments failed"); }
        finally
        {
            if (version == _loadVersion) IsLoading = false;
            foreach (var n in new[] { nameof(IsEmpty), nameof(EmptyMessage), nameof(PageText) }) OnPropertyChanged(n);
        }
    }

    [RelayCommand]
    private async Task AddAsync()
    {
        var editor = new PaymentEditorViewModel(_billing, _doctors, _log, SelectedDoctorId > 0 ? SelectedDoctorId : null);
        await editor.LoadAsync();
        if (_dialogs.ShowDialog(editor) == true) { Page = 1; await LoadAsync(); }
    }

    [RelayCommand]
    private void StartReverse(PaymentRow? row)
    {
        if (row is null || row.IsReversed) return;
        _pendingRow = row;
        ConfirmTitle = $"{Loc["Payments_ReverseTitle"]} {row.ReceiptNumber}";
        ReasonText = string.Empty; ErrorText = string.Empty; IsConfirmOpen = true;
    }

    [RelayCommand] private void PrintReceipt(PaymentRow? row) { if (row is not null) _dialogs.ShowPrintPreview(PrintKind.Receipt, row.Id); }

    [RelayCommand] private void CancelConfirm() => IsConfirmOpen = false;

    [RelayCommand]
    private async Task ConfirmReverseAsync()
    {
        if (_pendingRow is null) return;
        try
        {
            await _billing.ReversePaymentAsync(_pendingRow.Id, ReasonText);
            IsConfirmOpen = false;
            await LoadAsync();
        }
        catch (ValidationException ex)
        {
            ErrorText = string.Join("\n", ex.Errors.Select(e => Loc.GetOrNull("Val_" + e.ErrorCode) ?? Loc["Error_Generic"]).Distinct());
        }
        catch (Exception ex) { _log.LogError(ex, "Reversing payment failed"); ErrorText = Loc["Error_Generic"]; }
    }

    [RelayCommand] private async Task NextPageAsync() { if (Page < TotalPages) { Page++; await LoadAsync(); } }
    [RelayCommand] private async Task PreviousPageAsync() { if (Page > 1) { Page--; await LoadAsync(); } }
}
