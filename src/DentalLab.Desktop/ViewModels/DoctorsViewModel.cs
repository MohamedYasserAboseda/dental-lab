using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using DentalLab.Application.Abstractions;
using DentalLab.Application.Doctors;
using DentalLab.Desktop.Services;
using DentalLab.Shared;
using Microsoft.Extensions.Logging;

namespace DentalLab.Desktop.ViewModels;

public sealed partial class DoctorsViewModel : ObservableObject
{
    private const int PageSize = 12;
    private readonly IDoctorService _service;
    private readonly ICatalogService _catalog;
    private readonly ICaseService _cases;
    private readonly IBillingService _billing;
    private readonly IDialogService _dialogs;
    private readonly ILogger _log;
    private CancellationTokenSource? _debounce;
    private int _loadVersion;

    public LocalizationService Loc => LocalizationService.Instance;
    public ObservableCollection<DoctorListItem> Items { get; } = [];

    [ObservableProperty] private string _searchText = string.Empty;
    [ObservableProperty] private DoctorStatusFilter _statusFilter = DoctorStatusFilter.All;
    [ObservableProperty] private int _page = 1;
    [ObservableProperty] private int _totalPages = 1;
    [ObservableProperty] private int _totalCount;
    [ObservableProperty] private bool _isLoading;
    [ObservableProperty] private DoctorListItem? _selectedDoctor;
    [ObservableProperty] private DoctorProfileViewModel? _profile;

    public bool IsProfileOpen => Profile is not null;
    public bool IsEmpty => !IsLoading && Items.Count == 0;
    public bool IsFilterAll { get => StatusFilter == DoctorStatusFilter.All; set { if (value) StatusFilter = DoctorStatusFilter.All; } }
    public bool IsFilterActive { get => StatusFilter == DoctorStatusFilter.Active; set { if (value) StatusFilter = DoctorStatusFilter.Active; } }
    public bool IsFilterInactive { get => StatusFilter == DoctorStatusFilter.Inactive; set { if (value) StatusFilter = DoctorStatusFilter.Inactive; } }

    public string EmptyMessage => Loc[HasActiveFilter ? "Doctors_NoResults" : "Doctors_Empty"];
    public string PageText => string.Format(Loc["Page_Of"], Page, TotalPages);
    public string PrevGlyph => Loc.Language == AppLanguage.Arabic ? "\uE76C" : "\uE76B";
    public string NextGlyph => Loc.Language == AppLanguage.Arabic ? "\uE76B" : "\uE76C";
    private bool HasActiveFilter => !string.IsNullOrWhiteSpace(SearchText) || StatusFilter != DoctorStatusFilter.All;

    public DoctorsViewModel(IDoctorService service, ICatalogService catalog, ICaseService cases, IBillingService billing,
        IDialogService dialogs, ILoggerFactory loggers)
    {
        _cases = cases; _billing = billing;
        _service = service; _catalog = catalog; _dialogs = dialogs; _log = loggers.CreateLogger<DoctorsViewModel>();
        Loc.PropertyChanged += (_, _) =>
        {
            OnPropertyChanged(nameof(EmptyMessage));
            OnPropertyChanged(nameof(PageText));
            OnPropertyChanged(nameof(PrevGlyph));
            OnPropertyChanged(nameof(NextGlyph));
        };
    }

    public Task InitializeAsync() => LoadAsync();

    partial void OnProfileChanged(DoctorProfileViewModel? value) => OnPropertyChanged(nameof(IsProfileOpen));
    partial void OnIsLoadingChanged(bool value) => OnPropertyChanged(nameof(IsEmpty));

    partial void OnSearchTextChanged(string value)
    {
        _debounce?.Cancel();
        _debounce = new CancellationTokenSource();
        _ = DebouncedReloadAsync(_debounce.Token);
    }

    partial void OnStatusFilterChanged(DoctorStatusFilter value)
    {
        OnPropertyChanged(nameof(IsFilterAll));
        OnPropertyChanged(nameof(IsFilterActive));
        OnPropertyChanged(nameof(IsFilterInactive));
        Page = 1;
        _ = LoadAsync();
    }

    private async Task DebouncedReloadAsync(CancellationToken ct)
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
            var result = await _service.SearchAsync(new DoctorQuery(SearchText, StatusFilter, Page, PageSize));
            if (version != _loadVersion) return; // a newer load superseded this one

            Items.Clear();
            foreach (var d in result.Items) Items.Add(d);
            TotalCount = result.TotalCount;
            TotalPages = Math.Max(1, (int)Math.Ceiling(result.TotalCount / (double)PageSize));
        }
        catch (Exception ex) { _log.LogError(ex, "Loading doctors failed"); }
        finally
        {
            if (version == _loadVersion) IsLoading = false;
            OnPropertyChanged(nameof(IsEmpty));
            OnPropertyChanged(nameof(EmptyMessage));
            OnPropertyChanged(nameof(PageText));
        }
    }

    [RelayCommand]
    private async Task AddAsync()
    {
        var editor = new DoctorEditorViewModel(_service, _log);
        if (_dialogs.ShowDoctorEditor(editor) == true) { Page = 1; await LoadAsync(); }
    }

    [RelayCommand]
    private async Task EditAsync(DoctorListItem? item)
    {
        if (item is null) return;
        try
        {
            var details = await _service.GetAsync(item.Id);
            if (details is null) return;
            if (_dialogs.ShowDoctorEditor(new DoctorEditorViewModel(_service, _log, details)) == true)
                await LoadAsync();
        }
        catch (Exception ex) { _log.LogError(ex, "Opening editor failed"); }
    }

    [RelayCommand]
    private async Task OpenProfileAsync(DoctorListItem? item)
    {
        if (item is null) return;
        var profile = new DoctorProfileViewModel(_service, _catalog, _cases, _billing, _dialogs, _log, CloseProfile);
        await profile.LoadAsync(item.Id);
        Profile = profile;
    }

    private void CloseProfile()
    {
        Profile = null;
        _ = LoadAsync(); // refresh balances/status that may have changed
    }

    [RelayCommand]
    private async Task NextPageAsync()
    {
        if (Page >= TotalPages) return;
        Page++; await LoadAsync();
    }

    [RelayCommand]
    private async Task PreviousPageAsync()
    {
        if (Page <= 1) return;
        Page--; await LoadAsync();
    }
}
