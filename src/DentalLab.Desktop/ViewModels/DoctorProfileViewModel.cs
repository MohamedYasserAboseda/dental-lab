using System.Collections.ObjectModel;
using System.Globalization;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using DentalLab.Application.Abstractions;
using DentalLab.Application.Billing;
using DentalLab.Application.Cases;
using DentalLab.Application.Doctors;
using DentalLab.Application.Documents;

using DentalLab.Domain.Entities;
using DentalLab.Domain.Services;
using DentalLab.Desktop.Services;
using DentalLab.Shared;
using Microsoft.Extensions.Logging;

namespace DentalLab.Desktop.ViewModels;

public sealed class StatementLineItem(StatementLine line)
{
    private static string Money(decimal v) => v == 0 ? "" : v.ToString("N2", CultureInfo.InvariantCulture);

    public string DateText { get; } = DateTime.SpecifyKind(line.DateUtc, DateTimeKind.Utc).ToLocalTime()
        .ToString("yyyy-MM-dd HH:mm", CultureInfo.InvariantCulture);
    public LedgerEntryType Type { get; } = line.Type;
    public string Description { get; } = line.Description ?? "";
    public string DebitText { get; } = Money(line.Debit);
    public string CreditText { get; } = Money(line.Credit);
    public string RunningText { get; } = line.RunningBalance.ToString("N2", CultureInfo.InvariantCulture);
}

public sealed partial class DoctorProfileViewModel : ObservableObject
{
    private readonly IDoctorService _service;
    private readonly ICatalogService _catalog;
    private readonly ICaseService _cases;
    private readonly IBillingService _billing;
    private readonly IDialogService _dialogs;
    private readonly ILogger _log;
    private readonly Action _close;

    public LocalizationService Loc => LocalizationService.Instance;
    public ObservableCollection<StatementLineItem> Lines { get; } = [];
    public ObservableCollection<CaseRow> Cases { get; } = [];
    public ObservableCollection<InvoiceRow> Invoices { get; } = [];
    public ObservableCollection<PaymentRow> Payments { get; } = [];

    [ObservableProperty] private int _selectedTab;
    public bool IsTabStatement { get => SelectedTab == 0; set { if (value) SelectedTab = 0; } }
    public bool IsTabCases { get => SelectedTab == 1; set { if (value) SelectedTab = 1; } }
    public bool IsTabInvoices { get => SelectedTab == 2; set { if (value) SelectedTab = 2; } }
    public bool IsTabPayments { get => SelectedTab == 3; set { if (value) SelectedTab = 3; } }
    public bool HasNoCases => Cases.Count == 0;
    public bool HasNoInvoices => Invoices.Count == 0;
    public bool HasNoPayments => Payments.Count == 0;

    partial void OnSelectedTabChanged(int value)
    {
        foreach (var n in new[] { nameof(IsTabStatement), nameof(IsTabCases), nameof(IsTabInvoices), nameof(IsTabPayments) })
            OnPropertyChanged(n);
    }

    [ObservableProperty] private DoctorDetails? _details;

    public decimal Balance => Details?.Balance ?? 0;
    public string BalanceText => Balance.ToString("N2", CultureInfo.InvariantCulture);
    public string PhoneText => Fallback(Details?.Phone);
    public string EmailText => Fallback(Details?.Email);
    public string AddressText => Fallback(Details?.Address);
    public string NotesText => Fallback(Details?.Notes);
    public string RegisteredText => Details is null ? "" :
        DateTime.SpecifyKind(Details.RegisteredUtc, DateTimeKind.Utc).ToLocalTime()
            .ToString("yyyy-MM-dd", CultureInfo.InvariantCulture);
    public string ToggleActiveText => Loc[Details?.IsActive == true ? "Profile_Deactivate" : "Profile_Activate"];
    public bool HasNoLines => Lines.Count == 0;
    public string BackGlyph => Loc.Language == AppLanguage.Arabic ? "\uE72A" : "\uE72B";

    public DoctorProfileViewModel(IDoctorService service, ICatalogService catalog, ICaseService cases, IBillingService billing,
        IDialogService dialogs, ILogger log, Action close)
    {
        _cases = cases; _billing = billing;
        _service = service; _catalog = catalog; _dialogs = dialogs; _log = log; _close = close;
        Loc.PropertyChanged += (_, _) =>
        {
            OnPropertyChanged(nameof(ToggleActiveText));
            OnPropertyChanged(nameof(BackGlyph));
        };
    }

    private static string Fallback(string? s) => string.IsNullOrWhiteSpace(s) ? "—" : s;

    public async Task LoadAsync(int id)
    {
        try
        {
            Details = await _service.GetAsync(id);
            var statement = await _service.GetStatementAsync(id);
            Lines.Clear();
            foreach (var l in statement) Lines.Add(new StatementLineItem(l));
        }
        catch (Exception ex) { _log.LogError(ex, "Loading doctor profile {Id} failed", id); }

        try
        {
            var arabic = Loc.Language == AppLanguage.Arabic;
            var cs = await _cases.SearchAsync(new CaseQuery(null, id, null, null, null, false, 1, 50));
            Cases.Clear();
            foreach (var c in cs.Items)
                Cases.Add(new CaseRow(c, arabic && !string.IsNullOrWhiteSpace(c.RestorationNameAr) ? c.RestorationNameAr! : c.RestorationName));

            var inv = await _billing.SearchInvoicesAsync(new InvoiceQuery(null, id, null, 1, 50));
            Invoices.Clear();
            foreach (var i in inv.Items) Invoices.Add(new InvoiceRow(i));

            var pay = await _billing.SearchPaymentsAsync(new PaymentQuery(null, id, 1, 50));
            Payments.Clear();
            foreach (var p in pay.Items) Payments.Add(new PaymentRow(p));
        }
        catch (Exception ex) { _log.LogError(ex, "Loading the doctor's history failed"); }

        foreach (var n in new[] { nameof(HasNoCases), nameof(HasNoInvoices), nameof(HasNoPayments), nameof(Balance), nameof(BalanceText), nameof(PhoneText), nameof(EmailText),
                     nameof(AddressText), nameof(NotesText), nameof(RegisteredText), nameof(ToggleActiveText),
                     nameof(HasNoLines) })
            OnPropertyChanged(n);
    }

    [RelayCommand]
    private async Task PricesAsync()
    {
        if (Details is null) return;
        var vm = new DoctorPricesViewModel(_catalog, Details.Id, Details.FullName, _log);
        await vm.LoadAsync();
        _dialogs.ShowDialog(vm);
    }

    [RelayCommand] private void PrintStatement() { if (Details is not null) _dialogs.ShowPrintPreview(PrintKind.Statement, Details.Id); }

    [RelayCommand] private void Back() => _close();

    [RelayCommand]
    private async Task EditAsync()
    {
        if (Details is null) return;
        var editor = new DoctorEditorViewModel(_service, _log, Details);
        if (_dialogs.ShowDoctorEditor(editor) == true) await LoadAsync(Details.Id);
    }

    [RelayCommand]
    private async Task ToggleActiveAsync()
    {
        if (Details is null) return;
        try
        {
            await _service.SetActiveAsync(Details.Id, !Details.IsActive);
            await LoadAsync(Details.Id);
        }
        catch (Exception ex) { _log.LogError(ex, "Toggling doctor active state failed"); }
    }
}
