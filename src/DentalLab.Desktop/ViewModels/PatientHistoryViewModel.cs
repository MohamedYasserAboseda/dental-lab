using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.Input;
using DentalLab.Application.Abstractions;
using DentalLab.Application.Cases;
using DentalLab.Shared;
using Microsoft.Extensions.Logging;

namespace DentalLab.Desktop.ViewModels;

public sealed partial class PatientHistoryViewModel(ICaseService cases, int patientId, string patientName, ILogger log) : DialogViewModelBase
{
    public override string Title => $"{Loc["Patient_HistoryTitle"]} — {patientName}";
    public ObservableCollection<CaseRow> Items { get; } = [];
    public bool HasNoItems => Items.Count == 0;

    public async Task LoadAsync()
    {
        try
        {
            var arabic = Loc.Language == AppLanguage.Arabic;
            var r = await cases.SearchAsync(new CaseQuery(null, null, null, null, null, false, 1, 100) { PatientId = patientId });
            foreach (var c in r.Items)
                Items.Add(new CaseRow(c, arabic && !string.IsNullOrWhiteSpace(c.RestorationNameAr) ? c.RestorationNameAr! : c.RestorationName));
        }
        catch (Exception ex) { log.LogError(ex, "Loading the patient history failed"); }
        OnPropertyChanged(nameof(HasNoItems));
    }

    [RelayCommand] private void Close() => RequestClose(false);
}
