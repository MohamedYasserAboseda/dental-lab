using System.Collections.ObjectModel;
using System.Globalization;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using DentalLab.Application.Abstractions;
using DentalLab.Application.Cases;
using DentalLab.Application.Documents;

using DentalLab.Desktop.Services;
using DentalLab.Domain.Entities;
using DentalLab.Shared;
using Microsoft.Extensions.Logging;

namespace DentalLab.Desktop.ViewModels;

public enum CasePeriod { All, Today, Last7Days, ThisMonth }
public sealed record StatusOption(CaseStatus? Value, string Text);
public sealed record PeriodOption(CasePeriod Value, string Text);

public sealed class CaseRow(CaseListItem item, string restorationText)
{
    public int Id => item.Id;
    public string CaseNumber => item.CaseNumber;
    public string DoctorName => item.DoctorName;
    public string PatientName => item.PatientName;
    public string RestorationText => restorationText;
    public string TeethText => item.Teeth.Count == 0 ? "—" : string.Join(", ", item.Teeth);
    public string ExpectedText => item.ExpectedDeliveryDate.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture);
    public string TotalText => item.Total.ToString("N2", CultureInfo.InvariantCulture);
    public CaseStatus Status => item.Status;
    public bool IsOverdue => item.IsOverdue;
    public bool IsEditable => !CaseWorkflow.IsFinal(item.Status);
}

public sealed partial class CasesViewModel : ObservableObject
{
    private const int PageSize = 12;
    private readonly ICaseService _cases;
    private readonly IDoctorService _doctors;
    private readonly IPatientService _patients;
    private readonly ICatalogService _catalog;
    private readonly IBillingService _billing;
    private readonly IAttachmentService _attachments;
    private readonly FileDialogService _files;
    private readonly IDialogService _dialogs;
    private readonly ILogger _log;
    private CancellationTokenSource? _debounce;
    private int _loadVersion;
    private bool _building;

    public LocalizationService Loc => LocalizationService.Instance;
    public ObservableCollection<CaseRow> Items { get; } = [];
    public ObservableCollection<OptionItem> DoctorOptions { get; } = [];
    public ObservableCollection<StatusOption> StatusOptions { get; } = [];
    public ObservableCollection<PeriodOption> PeriodOptions { get; } = [];

    [ObservableProperty] private string _searchText = string.Empty;
    [ObservableProperty] private int _selectedDoctorId;
    [ObservableProperty] private StatusOption? _selectedStatusOption;
    [ObservableProperty] private PeriodOption? _selectedPeriodOption;
    [ObservableProperty] private bool _overdueOnly;
    [ObservableProperty] private int _page = 1;
    [ObservableProperty] private int _totalPages = 1;
    [ObservableProperty] private bool _isLoading;
    [ObservableProperty] private CaseRow? _selectedCase;
    [ObservableProperty] private CaseDetailsViewModel? _details;

    public bool IsDetailsOpen => Details is not null;
    public bool IsEmpty => !IsLoading && Items.Count == 0;
    private bool HasFilter => !string.IsNullOrWhiteSpace(SearchText) || SelectedDoctorId > 0 || OverdueOnly ||
                               SelectedStatusOption?.Value is not null || SelectedPeriodOption?.Value is not (null or CasePeriod.All);
    public string EmptyMessage => Loc[HasFilter ? "Cases_NoResults" : "Cases_Empty"];
    public string PageText => string.Format(Loc["Page_Of"], Page, TotalPages);
    public string PrevGlyph => Loc.Language == AppLanguage.Arabic ? "\uE76C" : "\uE76B";
    public string NextGlyph => Loc.Language == AppLanguage.Arabic ? "\uE76B" : "\uE76C";

    public CasesViewModel(ICaseService cases, IDoctorService doctors, IPatientService patients,
        ICatalogService catalog, IBillingService billing, IAttachmentService attachments, FileDialogService files,
        IDialogService dialogs, ILoggerFactory loggers)
    {
        _attachments = attachments; _files = files;
        _cases = cases; _doctors = doctors; _patients = patients; _catalog = catalog; _billing = billing; _dialogs = dialogs;
        _log = loggers.CreateLogger<CasesViewModel>();
        BuildOptions();
        Loc.PropertyChanged += (_, _) =>
        {
            BuildOptions();
            foreach (var n in new[] { nameof(EmptyMessage), nameof(PageText), nameof(PrevGlyph), nameof(NextGlyph) }) OnPropertyChanged(n);
            _ = LoadAsync(); // restoration names are language dependent
        };
    }

    private void BuildOptions()
    {
        _building = true;
        try
        {
            var status = SelectedStatusOption?.Value;
            var period = SelectedPeriodOption?.Value ?? CasePeriod.All;

            StatusOptions.Clear();
            StatusOptions.Add(new StatusOption(null, Loc["Filter_AllStatuses"]));
            foreach (var s in Enum.GetValues<CaseStatus>()) StatusOptions.Add(new StatusOption(s, Loc["Status_" + s]));
            SelectedStatusOption = StatusOptions.First(o => o.Value == status);

            PeriodOptions.Clear();
            foreach (var p in Enum.GetValues<CasePeriod>()) PeriodOptions.Add(new PeriodOption(p, Loc["Period_" + p]));
            SelectedPeriodOption = PeriodOptions.First(o => o.Value == period);
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

    partial void OnDetailsChanged(CaseDetailsViewModel? value) => OnPropertyChanged(nameof(IsDetailsOpen));
    partial void OnIsLoadingChanged(bool value) => OnPropertyChanged(nameof(IsEmpty));
    partial void OnSelectedDoctorIdChanged(int value) => FilterChanged();
    partial void OnSelectedStatusOptionChanged(StatusOption? value) => FilterChanged();
    partial void OnSelectedPeriodOptionChanged(PeriodOption? value) => FilterChanged();
    partial void OnOverdueOnlyChanged(bool value) => FilterChanged();

    private void FilterChanged()
    {
        if (_building) return;
        Page = 1;
        _ = LoadAsync();
    }

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

    private DateTime? FromUtc() => (SelectedPeriodOption?.Value ?? CasePeriod.All) switch
    {
        CasePeriod.Today => DateTime.Today.ToUniversalTime(),
        CasePeriod.Last7Days => DateTime.Today.AddDays(-6).ToUniversalTime(),
        CasePeriod.ThisMonth => new DateTime(DateTime.Today.Year, DateTime.Today.Month, 1).ToUniversalTime(),
        _ => null
    };

    private async Task LoadAsync()
    {
        var version = ++_loadVersion;
        IsLoading = true;
        try
        {
            var arabic = Loc.Language == AppLanguage.Arabic;
            var query = new CaseQuery(SearchText, SelectedDoctorId > 0 ? SelectedDoctorId : null, SelectedStatusOption?.Value,
                FromUtc(), null, OverdueOnly, Page, PageSize);
            var r = await _cases.SearchAsync(query);
            if (version != _loadVersion) return;

            Items.Clear();
            foreach (var c in r.Items)
                Items.Add(new CaseRow(c, arabic && !string.IsNullOrWhiteSpace(c.RestorationNameAr) ? c.RestorationNameAr! : c.RestorationName));
            TotalPages = Math.Max(1, (int)Math.Ceiling(r.TotalCount / (double)PageSize));
        }
        catch (Exception ex) { _log.LogError(ex, "Loading cases failed"); }
        finally
        {
            if (version == _loadVersion) IsLoading = false;
            foreach (var n in new[] { nameof(IsEmpty), nameof(EmptyMessage), nameof(PageText) }) OnPropertyChanged(n);
        }
    }

    private CaseEditorViewModel CreateEditor(CaseDetails? existing)
        => new(_cases, _doctors, _patients, _catalog, _log, existing);

    private async Task<bool> ShowEditorAsync(CaseDetails? existing)
    {
        var editor = CreateEditor(existing);
        await editor.LoadAsync();
        return _dialogs.ShowDialog(editor) == true;
    }

    private async Task<bool> ShowInvoiceEditorAsync(CaseDetails c)
    {
        var editor = new InvoiceEditorViewModel(_billing, _doctors, _log, c.DoctorId, c.Id);
        await editor.LoadAsync();
        return _dialogs.ShowDialog(editor) == true;
    }

    [RelayCommand]
    private async Task AddAsync()
    {
        if (await ShowEditorAsync(null)) { Page = 1; await LoadAsync(); }
    }

    [RelayCommand]
    private async Task EditAsync(CaseRow? row)
    {
        if (row is null) return;
        try
        {
            var details = await _cases.GetAsync(row.Id);
            if (details is null || !details.IsEditable || details.IsInvoiced) return;
            if (await ShowEditorAsync(details)) await LoadAsync();
        }
        catch (Exception ex) { _log.LogError(ex, "Opening case editor failed"); }
    }

    [RelayCommand]
    private async Task OpenDetailsAsync(CaseRow? row)
    {
        if (row is null) return;
        var vm = new CaseDetailsViewModel(_cases, _log, CloseDetails, ShowEditorAsync, ShowInvoiceEditorAsync,
            id => _dialogs.ShowPrintPreview(PrintKind.WorkOrder, id), new AttachmentsViewModel(_attachments, _files, _log));
        await vm.LoadAsync(row.Id);
        Details = vm;
    }

    private void CloseDetails()
    {
        Details = null;
        _ = LoadAsync();
    }

    [RelayCommand] private async Task NextPageAsync() { if (Page < TotalPages) { Page++; await LoadAsync(); } }
    [RelayCommand] private async Task PreviousPageAsync() { if (Page > 1) { Page--; await LoadAsync(); } }
}
