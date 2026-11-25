using System.Collections.ObjectModel;
using System.Globalization;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using DentalLab.Application.Abstractions;
using DentalLab.Desktop.Helpers;
using DentalLab.Shared;
using FluentValidation;
using Microsoft.Extensions.Logging;

namespace DentalLab.Desktop.ViewModels;

public sealed partial class PriceRow : ObservableObject
{
    public int TypeId { get; init; }
    public string Name { get; init; } = "";
    public string DefaultText { get; init; } = "";
    [ObservableProperty] private string _priceText = "";
}

public sealed partial class DoctorPricesViewModel : DialogViewModelBase
{
    private readonly ICatalogService _catalog;
    private readonly int _doctorId;
    private readonly string _doctorName;
    private readonly ILogger _log;

    public override string Title => $"{Loc["Prices_Title"]} — {_doctorName}";
    public ObservableCollection<PriceRow> Rows { get; } = [];
    [ObservableProperty] private string _errorText = string.Empty;

    public DoctorPricesViewModel(ICatalogService catalog, int doctorId, string doctorName, ILogger log)
    { _catalog = catalog; _doctorId = doctorId; _doctorName = doctorName; _log = log; }

    public async Task LoadAsync()
    {
        try
        {
            var arabic = Loc.Language == AppLanguage.Arabic;
            foreach (var i in await _catalog.GetDoctorPricesAsync(_doctorId))
                Rows.Add(new PriceRow
                {
                    TypeId = i.RestorationTypeId,
                    Name = i.Display(arabic),
                    DefaultText = i.DefaultPrice.ToString("N2", CultureInfo.InvariantCulture),
                    PriceText = i.DoctorPrice?.ToString("0.00", CultureInfo.InvariantCulture) ?? ""
                });
        }
        catch (Exception ex) { _log.LogError(ex, "Loading doctor prices failed"); }
    }

    [RelayCommand]
    private async Task SaveAsync()
    {
        ErrorText = string.Empty;
        var prices = new Dictionary<int, decimal?>();
        foreach (var r in Rows)
        {
            if (string.IsNullOrWhiteSpace(r.PriceText)) { prices[r.TypeId] = null; continue; }
            if (!NumberParser.TryParseMoney(r.PriceText, out var v)) { ErrorText = Loc["Val_NumberInvalid"]; return; }
            prices[r.TypeId] = v;
        }
        try { await _catalog.SetDoctorPricesAsync(_doctorId, prices); RequestClose(true); }
        catch (ValidationException ex) { ErrorText = ErrorsToText(ex); }
        catch (Exception ex) { _log.LogError(ex, "Saving doctor prices failed"); ErrorText = Loc["Error_Generic"]; }
    }

    [RelayCommand] private void Cancel() => RequestClose(false);
}
