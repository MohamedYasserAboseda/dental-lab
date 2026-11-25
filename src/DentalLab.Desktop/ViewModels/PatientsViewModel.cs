using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using DentalLab.Application.Abstractions;
using DentalLab.Application.Patients;
using DentalLab.Desktop.Services;
using DentalLab.Shared;
using Microsoft.Extensions.Logging;

namespace DentalLab.Desktop.ViewModels;

public sealed partial class PatientsViewModel : ObservableObject
{
    private const int PageSize = 12;
    private readonly IPatientService _patients;
    private readonly IDoctorService _doctors;
    private readonly ICaseService _cases;
    private readonly IDialogService _dialogs;
    private readonly ILogger _log;
    private CancellationTokenSource? _debounce;
    private int _loadVersion;

    public LocalizationService Loc => LocalizationService.Instance;
    public ObservableCollection<PatientListItem> Items { get; } = [];
    public ObservableCollection<OptionItem> DoctorOptions { get; } = [];

    [ObservableProperty] private string _searchText = string.Empty;
    [ObservableProperty] private int _selectedDoctorId;
    [ObservableProperty] private int _page = 1;
    [ObservableProperty] private int _totalPages = 1;
    [ObservableProperty] private bool _isLoading;
    [ObservableProperty] private PatientListItem? _selectedPatient;

    public bool IsEmpty => !IsLoading && Items.Count == 0;
    public string EmptyMessage => Loc[SelectedDoctorId > 0 || !string.IsNullOrWhiteSpace(SearchText) ? "Patients_NoResults" : "Patients_Empty"];
    public string PageText => string.Format(Loc["Page_Of"], Page, TotalPages);
    public string PrevGlyph => Loc.Language == AppLanguage.Arabic ? "\uE76C" : "\uE76B";
    public string NextGlyph => Loc.Language == AppLanguage.Arabic ? "\uE76B" : "\uE76C";

    public PatientsViewModel(IPatientService patients, IDoctorService doctors, ICaseService cases, IDialogService dialogs, ILoggerFactory loggers)
    {
        _cases = cases;
        _patients = patients; _doctors = doctors; _dialogs = dialogs; _log = loggers.CreateLogger<PatientsViewModel>();
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
            var r = await _patients.SearchAsync(new PatientQuery(SearchText, SelectedDoctorId > 0 ? SelectedDoctorId : null, Page, PageSize));
            if (version != _loadVersion) return;
            Items.Clear();
            foreach (var p in r.Items) Items.Add(p);
            TotalPages = Math.Max(1, (int)Math.Ceiling(r.TotalCount / (double)PageSize));
        }
        catch (Exception ex) { _log.LogError(ex, "Loading patients failed"); }
        finally
        {
            if (version == _loadVersion) IsLoading = false;
            foreach (var n in new[] { nameof(IsEmpty), nameof(EmptyMessage), nameof(PageText) }) OnPropertyChanged(n);
        }
    }

    [RelayCommand]
    private async Task AddAsync()
    {
        var editor = new PatientEditorViewModel(_patients, _doctors, _log, defaultDoctorId: SelectedDoctorId > 0 ? SelectedDoctorId : null);
        await editor.LoadAsync();
        if (_dialogs.ShowDialog(editor) == true) { Page = 1; await LoadAsync(); }
    }

    [RelayCommand]
    private async Task EditAsync(PatientListItem? item)
    {
        if (item is null) return;
        try
        {
            var details = await _patients.GetAsync(item.Id);
            if (details is null) return;
            var editor = new PatientEditorViewModel(_patients, _doctors, _log, details);
            await editor.LoadAsync();
            if (_dialogs.ShowDialog(editor) == true) await LoadAsync();
        }
        catch (Exception ex) { _log.LogError(ex, "Opening patient editor failed"); }
    }

    [RelayCommand]
    private async Task OpenHistoryAsync(PatientListItem? item)
    {
        if (item is null) return;
        var vm = new PatientHistoryViewModel(_cases, item.Id, item.Name, _log);
        await vm.LoadAsync();
        _dialogs.ShowDialog(vm);
    }

    [RelayCommand] private async Task NextPageAsync() { if (Page < TotalPages) { Page++; await LoadAsync(); } }
    [RelayCommand] private async Task PreviousPageAsync() { if (Page > 1) { Page--; await LoadAsync(); } }
}
