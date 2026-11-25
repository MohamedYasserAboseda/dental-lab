using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using DentalLab.Application;
using DentalLab.Application.Abstractions;
using DentalLab.Application.Catalog;
using DentalLab.Desktop.Helpers;
using DentalLab.Desktop.Motion;
using DentalLab.Desktop.Services;
using FluentValidation;
using Microsoft.Extensions.Logging;
using System.Globalization;

namespace DentalLab.Desktop.ViewModels;

public sealed partial class CatalogItemEditorViewModel : DialogViewModelBase
{
    private readonly ICatalogService _catalog;
    private readonly ILogger _log;
    private readonly bool _isRestoration;
    private readonly int? _id;

    public bool ShowPrice => _isRestoration;
    public override string Title => Loc[(_id.HasValue ? "Catalog_Edit" : "Catalog_Add") + (_isRestoration ? "Type" : "Material")];

    [ObservableProperty] private string _name = string.Empty;
    [ObservableProperty] private string _nameAr = string.Empty;
    [ObservableProperty] private string _priceText = "0.00";
    [ObservableProperty] private bool _isActive = true;
    [ObservableProperty] private string _errorText = string.Empty;

    public CatalogItemEditorViewModel(ICatalogService catalog, bool isRestoration, ILogger log, CatalogItem? existing = null)
    {
        _catalog = catalog; _isRestoration = isRestoration; _log = log;
        if (existing is null) return;
        _id = existing.Id;
        Name = existing.Name; NameAr = existing.NameAr ?? "";
        PriceText = existing.DefaultPrice.ToString("0.00", CultureInfo.InvariantCulture);
        IsActive = existing.IsActive;
    }

    [RelayCommand]
    private async Task SaveAsync()
    {
        ErrorText = string.Empty;
        decimal price = 0;
        if (_isRestoration && !NumberParser.TryParseMoney(PriceText, out price)) { ErrorText = Loc["Val_NumberInvalid"]; return; }

        var input = new CatalogItemInput(Name, NameAr, price, IsActive);
        try
        {
            if (_isRestoration) await _catalog.SaveRestorationTypeAsync(_id, input);
            else await _catalog.SaveMaterialAsync(_id, input);
            RequestClose(true);
        }
        catch (ValidationException ex) { ErrorText = ErrorsToText(ex); }
        catch (Exception ex) { _log.LogError(ex, "Saving catalog item failed"); ErrorText = Loc["Error_Generic"]; }
    }

    [RelayCommand] private void Cancel() => RequestClose(false);
}

public sealed partial class CatalogViewModel : ObservableObject
{
    private readonly ICatalogService _catalog;
    private readonly IDialogService _dialogs;
    private readonly ILogger _log;

    public LocalizationService Loc => LocalizationService.Instance;
    public ObservableCollection<CatalogItem> Items { get; } = [];

    [ObservableProperty] private bool _isRestoration = true;
    public bool IsMaterials { get => !IsRestoration; set { if (value) IsRestoration = false; } }

    public CatalogViewModel(ICatalogService catalog, IDialogService dialogs, ILogger log)
    { _catalog = catalog; _dialogs = dialogs; _log = log; }

    partial void OnIsRestorationChanged(bool value)
    {
        OnPropertyChanged(nameof(IsMaterials));
        _ = LoadAsync();
    }

    public async Task LoadAsync()
    {
        try
        {
            var list = IsRestoration ? await _catalog.GetRestorationTypesAsync(false) : await _catalog.GetMaterialsAsync(false);
            Items.Clear();
            foreach (var i in list) Items.Add(i);
        }
        catch (Exception ex) { _log.LogError(ex, "Loading catalog failed"); }
    }

    [RelayCommand]
    private async Task AddAsync()
    {
        if (_dialogs.ShowDialog(new CatalogItemEditorViewModel(_catalog, IsRestoration, _log)) == true) await LoadAsync();
    }

    [RelayCommand]
    private async Task EditAsync(CatalogItem? item)
    {
        if (item is null) return;
        if (_dialogs.ShowDialog(new CatalogItemEditorViewModel(_catalog, IsRestoration, _log, item)) == true) await LoadAsync();
    }
}

public sealed partial class SettingsViewModel : ObservableObject
{
    private readonly ISettingsService _settings;
    private readonly ILogger _log;

    public LocalizationService Loc => LocalizationService.Instance;
    public CatalogViewModel Catalog { get; }
    public LabSettingsViewModel Lab { get; }

    [ObservableProperty] private bool _reduceMotion = MotionService.ReducedMotion;

    public SettingsViewModel(ISettingsService settings, ICatalogService catalog, IDialogService dialogs,
        ILabSettingsService labs, FileDialogService files, ILoggerFactory loggers)
    {
        _settings = settings;
        _log = loggers.CreateLogger<SettingsViewModel>();
        Catalog = new CatalogViewModel(catalog, dialogs, _log);
        Lab = new LabSettingsViewModel(labs, files, _log);
    }

    public Task InitializeAsync() => Task.WhenAll(Catalog.LoadAsync(), Lab.InitializeAsync());

    partial void OnReduceMotionChanged(bool value)
    {
        MotionService.ReducedMotion = value;
        _ = PersistAsync(value);
    }

    private async Task PersistAsync(bool value)
    {
        try { await _settings.SetAsync(SettingKeys.ReducedMotion, value.ToString()); }
        catch (Exception ex) { _log.LogError(ex, "Persisting reduced motion failed"); }
    }
}
