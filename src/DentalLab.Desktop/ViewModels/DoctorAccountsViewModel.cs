using System.Collections.ObjectModel;
using System.Globalization;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using DentalLab.Application.Abstractions;
using DentalLab.Application.Documents;

using DentalLab.Application.Doctors;
using DentalLab.Desktop.Services;
using Microsoft.Extensions.Logging;

namespace DentalLab.Desktop.ViewModels;

/// <summary>Balances of all doctors (left) and the statement of the selected doctor (right).</summary>
public sealed partial class DoctorAccountsViewModel : ObservableObject
{
    private static readonly CultureInfo Inv = CultureInfo.InvariantCulture;
    private readonly IDoctorService _doctors;
    private readonly IBillingService _billing;
    private readonly IDialogService _dialogs;
    private readonly ILogger _log;
    private CancellationTokenSource? _debounce;
    private int _loadVersion;

    public LocalizationService Loc => LocalizationService.Instance;
    public ObservableCollection<DoctorListItem> Items { get; } = [];
    public ObservableCollection<StatementLineItem> Lines { get; } = [];

    [ObservableProperty] private string _searchText = string.Empty;
    [ObservableProperty] private bool _balancesOnly;
    [ObservableProperty] private DoctorListItem? _selectedDoctor;
    [ObservableProperty] private decimal _totalOutstanding;
    [ObservableProperty] private decimal _totalCredit;
    [ObservableProperty] private decimal _selectedBalance;

    public bool HasSelection => SelectedDoctor is not null;
    public bool HasNoSelection => SelectedDoctor is null;
    public bool HasNoLines => Lines.Count == 0;
    public string OutstandingText => TotalOutstanding.ToString("N2", Inv);
    public string CreditText => TotalCredit.ToString("N2", Inv);
    public string SelectedBalanceText => SelectedBalance.ToString("N2", Inv);
    public string SelectedName => SelectedDoctor?.FullName ?? "";

    public DoctorAccountsViewModel(IDoctorService doctors, IBillingService billing, IDialogService dialogs, ILoggerFactory loggers)
    {
        _doctors = doctors; _billing = billing; _dialogs = dialogs; _log = loggers.CreateLogger<DoctorAccountsViewModel>();
    }

    public Task InitializeAsync() => LoadAsync();

    partial void OnBalancesOnlyChanged(bool value) => _ = LoadAsync();
    partial void OnTotalOutstandingChanged(decimal value) => OnPropertyChanged(nameof(OutstandingText));
    partial void OnTotalCreditChanged(decimal value) => OnPropertyChanged(nameof(CreditText));
    partial void OnSelectedBalanceChanged(decimal value) => OnPropertyChanged(nameof(SelectedBalanceText));

    partial void OnSearchTextChanged(string value)
    {
        _debounce?.Cancel();
        _debounce = new CancellationTokenSource();
        _ = DebouncedAsync(_debounce.Token);
    }

    private async Task DebouncedAsync(CancellationToken ct)
    {
        try { await Task.Delay(250, ct); await LoadAsync(); }
        catch (OperationCanceledException) { }
    }

    partial void OnSelectedDoctorChanged(DoctorListItem? value)
    {
        OnPropertyChanged(nameof(HasSelection));
        OnPropertyChanged(nameof(HasNoSelection));
        OnPropertyChanged(nameof(SelectedName));
        _ = LoadStatementAsync();
    }

    private async Task LoadAsync()
    {
        var version = ++_loadVersion;
        try
        {
            var keep = SelectedDoctor?.Id;
            // One generous page: the account overview is meant for a lab's own doctors (hundreds, not millions).
            var r = await _doctors.SearchAsync(new DoctorQuery(SearchText, DoctorStatusFilter.All, 1, 200));
            if (version != _loadVersion) return;

            var list = BalancesOnly ? r.Items.Where(d => d.Balance != 0).ToList() : r.Items.ToList();
            Items.Clear();
            foreach (var d in list) Items.Add(d);
            TotalOutstanding = list.Where(d => d.Balance > 0).Sum(d => d.Balance);
            TotalCredit = -list.Where(d => d.Balance < 0).Sum(d => d.Balance);
            SelectedDoctor = keep is null ? null : Items.FirstOrDefault(d => d.Id == keep);
        }
        catch (Exception ex) { _log.LogError(ex, "Loading accounts failed"); }
    }

    private async Task LoadStatementAsync()
    {
        Lines.Clear();
        if (SelectedDoctor is not { } doctor) { SelectedBalance = 0; OnPropertyChanged(nameof(HasNoLines)); return; }
        try
        {
            foreach (var l in await _doctors.GetStatementAsync(doctor.Id)) Lines.Add(new StatementLineItem(l));
            SelectedBalance = doctor.Balance;
        }
        catch (Exception ex) { _log.LogError(ex, "Loading statement failed"); }
        OnPropertyChanged(nameof(HasNoLines));
    }

    [RelayCommand] private void PrintStatement() { if (SelectedDoctor is { } d) _dialogs.ShowPrintPreview(PrintKind.Statement, d.Id); }

    [RelayCommand]
    private async Task AddPaymentAsync()
    {
        if (SelectedDoctor is not { } d) return;
        var editor = new PaymentEditorViewModel(_billing, _doctors, _log, d.Id);
        await editor.LoadAsync();
        if (_dialogs.ShowDialog(editor) == true) await LoadAsync();
    }

    [RelayCommand]
    private async Task AddAdjustmentAsync()
    {
        if (SelectedDoctor is not { } d) return;
        if (_dialogs.ShowDialog(new AdjustmentEditorViewModel(_billing, _log, d.Id, d.FullName)) == true) await LoadAsync();
    }
}
