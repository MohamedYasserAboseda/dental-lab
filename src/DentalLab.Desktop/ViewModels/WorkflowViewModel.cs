using System.Collections.ObjectModel;
using System.Globalization;
using System.Windows.Media;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using DentalLab.Application.Abstractions;
using DentalLab.Application.Documents;
using DentalLab.Application.Workflow;
using DentalLab.Desktop.Services;
using DentalLab.Domain.Entities;
using DentalLab.Shared;
using FluentValidation;
using Microsoft.Extensions.Logging;

namespace DentalLab.Desktop.ViewModels;

public sealed record BoardCardVm(int Id, string CaseNumber, string PatientName, string DoctorName, string RestorationText,
    string TeethText, string ExpectedText, bool IsOverdue, CaseStatus? Next, string NextText)
{
    public bool HasNext => Next is not null;
}

public sealed record BoardColumnVm(CaseStatus Status, string Title, int Count, Brush Accent, ObservableCollection<BoardCardVm> Cards)
{
    public bool IsEmpty => Cards.Count == 0;
}

public sealed partial class WorkflowViewModel : ObservableObject
{
    /// <summary>The normal next stage of each production step. Rework or cancelling is done from the case page.</summary>
    private static readonly Dictionary<CaseStatus, CaseStatus> NextStage = new()
    {
        [CaseStatus.Received] = CaseStatus.Designing,
        [CaseStatus.Designing] = CaseStatus.Production,
        [CaseStatus.Production] = CaseStatus.Finishing,
        [CaseStatus.Finishing] = CaseStatus.QualityControl,
        [CaseStatus.QualityControl] = CaseStatus.Ready,
        [CaseStatus.Ready] = CaseStatus.Delivered
    };

    private readonly IWorkflowService _workflow;
    private readonly ICaseService _cases;
    private readonly IDoctorService _doctors;
    private readonly IDialogService _dialogs;
    private readonly ILogger _log;
    private int _loadVersion;

    public LocalizationService Loc => LocalizationService.Instance;
    public ObservableCollection<BoardColumnVm> Columns { get; } = [];
    public ObservableCollection<StatCard> Stats { get; } = [];
    public ObservableCollection<OptionItem> DoctorOptions { get; } = [];

    [ObservableProperty] private int _selectedDoctorId;
    [ObservableProperty] private bool _overdueOnly;
    [ObservableProperty] private bool _isLoading;
    [ObservableProperty] private string _statusText = string.Empty;

    public bool HasStatus => !string.IsNullOrEmpty(StatusText);

    /// <summary>Raised after every reload so the view can fade the board in.</summary>
    public event Action? Reloaded;

    public WorkflowViewModel(IWorkflowService workflow, ICaseService cases, IDoctorService doctors, IDialogService dialogs, ILoggerFactory loggers)
    {
        _workflow = workflow; _cases = cases; _doctors = doctors; _dialogs = dialogs; _log = loggers.CreateLogger<WorkflowViewModel>();
        Loc.PropertyChanged += (_, _) => _ = LoadAsync();
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

    partial void OnStatusTextChanged(string value) => OnPropertyChanged(nameof(HasStatus));
    partial void OnSelectedDoctorIdChanged(int value) => _ = LoadAsync();
    partial void OnOverdueOnlyChanged(bool value) => _ = LoadAsync();

    [RelayCommand] private Task RefreshAsync() => LoadAsync();

    private async Task LoadAsync()
    {
        var version = ++_loadVersion;
        IsLoading = true;
        try
        {
            var board = await _workflow.GetBoardAsync(new BoardQuery(SelectedDoctorId > 0 ? SelectedDoctorId : null, OverdueOnly));
            if (version != _loadVersion) return;
            Build(board);
            Reloaded?.Invoke();
        }
        catch (Exception ex) { _log.LogError(ex, "Loading the production board failed"); }
        finally { if (version == _loadVersion) IsLoading = false; }
    }

    private static Brush B(string key) => (Brush)System.Windows.Application.Current.FindResource(key);

    private void Build(BoardData board)
    {
        var arabic = Loc.Language == AppLanguage.Arabic;
        var arrow = arabic ? "←" : "→";

        Columns.Clear();
        foreach (var col in board.Columns)
        {
            var cards = new ObservableCollection<BoardCardVm>(col.Cards.Select(c =>
            {
                CaseStatus? next = NextStage.TryGetValue(c.Status, out var n) ? n : null;
                var nextText = next is null ? "" : next == CaseStatus.Delivered ? Loc["Workflow_Deliver"] : $"{arrow} {Loc["Status_" + next]}";
                return new BoardCardVm(c.CaseId, c.CaseNumber, c.PatientName, c.DoctorName,
                    arabic && !string.IsNullOrWhiteSpace(c.RestorationNameAr) ? c.RestorationNameAr! : c.RestorationName,
                    c.Teeth.Count == 0 ? "—" : string.Join(", ", c.Teeth),
                    c.ExpectedDeliveryDate.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture), c.IsOverdue, next, nextText);
            }));
            var accent = B(col.Status switch { CaseStatus.Ready => "SuccessBrush", CaseStatus.Received => "WarningBrush", _ => "GoldBrush" });
            Columns.Add(new BoardColumnVm(col.Status, Loc["Status_" + col.Status], col.TotalCount, accent, cards));
        }

        var s = board.Stats;
        Stats.Clear();
        Stats.Add(new StatCard(Loc["Workflow_Stat_InProgress"], "\uE895", s.InProgress, 0, B("GoldBrush"), null));
        Stats.Add(new StatCard(Loc["Workflow_Stat_DueToday"], "\uE787", s.DueToday, 0, B("WarningBrush"), null));
        Stats.Add(new StatCard(Loc["Workflow_Stat_Overdue"], "\uE7BA", s.Overdue, 0, B("DangerBrush"), null));
        Stats.Add(new StatCard(Loc["Workflow_Stat_Delivered30"], "\uE73E", s.DeliveredLast30Days, 0, B("SuccessBrush"), null));
        Stats.Add(new StatCard(Loc["Workflow_Stat_Turnaround"], "\uE823", s.AverageTurnaroundDays ?? 0, 1, B("GoldBrush"),
            s.AverageTurnaroundDays is null ? Loc["Dash_NoData"] : null));
    }

    [RelayCommand]
    private async Task AdvanceAsync(BoardCardVm? card)
    {
        if (card?.Next is not { } next) return;
        try
        {
            await _cases.ChangeStatusAsync(card.Id, next, null);
            StatusText = string.Empty;
        }
        catch (ValidationException ex)
        {
            StatusText = $"{card.CaseNumber}: " + string.Join(" ", ex.Errors.Select(e => Loc.GetOrNull("Val_" + e.ErrorCode) ?? Loc["Error_Generic"]).Distinct());
        }
        catch (Exception ex) { _log.LogError(ex, "Advancing a case failed"); StatusText = Loc["Error_Generic"]; }
        await LoadAsync();
    }

    [RelayCommand]
    private void PrintWorkOrder(BoardCardVm? card)
    {
        if (card is not null) _dialogs.ShowPrintPreview(PrintKind.WorkOrder, card.Id);
    }
}
