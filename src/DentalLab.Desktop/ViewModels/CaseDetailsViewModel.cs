using System.Collections.ObjectModel;
using System.Globalization;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using DentalLab.Application.Abstractions;
using DentalLab.Application.Cases;
using DentalLab.Desktop.Services;
using DentalLab.Domain.Entities;
using DentalLab.Shared;
using FluentValidation;
using Microsoft.Extensions.Logging;

namespace DentalLab.Desktop.ViewModels;

public sealed record WorkflowStepItem(string Text, bool IsDone, bool IsCurrent);
public sealed record StatusActionItem(CaseStatus Target, string Text, bool IsDanger);
public sealed record HistoryLine(CaseStatus? From, CaseStatus To, string DateText, string Note);

public sealed partial class CaseDetailsViewModel : ObservableObject
{
    private static readonly CultureInfo Inv = CultureInfo.InvariantCulture;
    private readonly ICaseService _service;
    private readonly ILogger _log;
    private readonly Action _close;
    private readonly Func<CaseDetails, Task<bool>> _edit;
    private readonly Func<CaseDetails, Task<bool>> _invoice;
    private readonly Action<int> _printWorkOrder;

    public AttachmentsViewModel Attachments { get; }

    public LocalizationService Loc => LocalizationService.Instance;
    public ObservableCollection<WorkflowStepItem> Steps { get; } = [];
    public ObservableCollection<StatusActionItem> Actions { get; } = [];
    public ObservableCollection<HistoryLine> History { get; } = [];

    [ObservableProperty] private CaseDetails? _details;
    [ObservableProperty] private string _noteText = string.Empty;
    [ObservableProperty] private string _errorText = string.Empty;
    [ObservableProperty] private double _progressFraction;

    public IReadOnlyList<int> Teeth => Details?.Teeth ?? Array.Empty<int>();
    public bool IsCancelled => Details?.Status == CaseStatus.Cancelled;
    public bool CanEdit => Details is { IsEditable: true, IsInvoiced: false };
    public bool IsInvoiced => Details?.IsInvoiced == true;
    public bool CanInvoice => Details is { IsInvoiced: false } d && d.Status != CaseStatus.Cancelled;
    public bool HasActions => Actions.Count > 0;
    public string BackGlyph => Loc.Language == AppLanguage.Arabic ? "\uE72A" : "\uE72B";

    public string RestorationText => Details is null ? "" : Display(Details.RestorationName, Details.RestorationNameAr);
    public string MaterialText => Details?.MaterialName is null ? "—" : Display(Details.MaterialName, Details.MaterialNameAr);
    public string ShadeText => string.IsNullOrWhiteSpace(Details?.Shade) ? "—" : Details!.Shade!;
    public string NotesText => string.IsNullOrWhiteSpace(Details?.Notes) ? "—" : Details!.Notes!;
    public string ReceivedText => Local(Details?.ReceivedUtc, "yyyy-MM-dd HH:mm");
    public string ExpectedText => Details?.ExpectedDeliveryDate.ToString("yyyy-MM-dd", Inv) ?? "";
    public string DeliveredText => Details?.ActualDeliveryUtc is { } d ? Local(d, "yyyy-MM-dd HH:mm") : "—";
    public string TeethText => Teeth.Count == 0 ? "—" : string.Join(", ", Teeth);
    public string SubtotalText => Details is null ? "" : (Details.Quantity * Details.UnitPrice).ToString("N2", Inv);
    public string UnitPriceText => Details?.UnitPrice.ToString("N2", Inv) ?? "";
    public string DiscountText => Details?.Discount.ToString("N2", Inv) ?? "";
    public string TotalText => Details?.Total.ToString("N2", Inv) ?? "";
    public string QuantityText => Details?.Quantity.ToString(Inv) ?? "";

    public CaseDetailsViewModel(ICaseService service, ILogger log, Action close, Func<CaseDetails, Task<bool>> edit,
        Func<CaseDetails, Task<bool>> invoice, Action<int> printWorkOrder, AttachmentsViewModel attachments)
    {
        Attachments = attachments;
        _service = service; _log = log; _close = close; _edit = edit; _invoice = invoice; _printWorkOrder = printWorkOrder;
        Loc.PropertyChanged += (_, _) => OnPropertyChanged(nameof(BackGlyph));
    }

    private string Display(string name, string? nameAr) => Loc.Language == AppLanguage.Arabic && !string.IsNullOrWhiteSpace(nameAr) ? nameAr! : name;
    private static string Local(DateTime? utc, string fmt)
        => utc is null ? "" : DateTime.SpecifyKind(utc.Value, DateTimeKind.Utc).ToLocalTime().ToString(fmt, Inv);

    public async Task LoadAsync(int id)
    {
        try
        {
            Details = await _service.GetAsync(id);
            if (Details is null) return;
            await Attachments.LoadAsync(id);

            var status = Details.Status;
            var reached = Details.History.Select(h => h.To).ToHashSet();
            var steps = CaseWorkflow.Steps;
            var idx = steps.ToList().IndexOf(status);

            Steps.Clear();
            for (var i = 0; i < steps.Count; i++)
            {
                var done = status == CaseStatus.Cancelled ? reached.Contains(steps[i]) : i < idx;
                Steps.Add(new WorkflowStepItem(Loc["Status_" + steps[i]], done, status != CaseStatus.Cancelled && i == idx));
            }
            var lastReached = status == CaseStatus.Cancelled
                ? steps.ToList().FindLastIndex(s => reached.Contains(s)) : idx;
            ProgressFraction = steps.Count <= 1 ? 0 : Math.Max(0, lastReached) / (double)(steps.Count - 1);

            Actions.Clear();
            foreach (var next in CaseWorkflow.AllowedNext(status))
                Actions.Add(new StatusActionItem(next, Loc["Status_" + next], next == CaseStatus.Cancelled));

            History.Clear();
            foreach (var h in Details.History)
                History.Add(new HistoryLine(h.From, h.To, Local(h.ChangedUtc, "yyyy-MM-dd HH:mm"), h.Note ?? ""));
        }
        catch (Exception ex) { _log.LogError(ex, "Loading case {Id} failed", id); }

        foreach (var n in new[]
                 {
                     nameof(Teeth), nameof(IsCancelled), nameof(CanEdit), nameof(IsInvoiced), nameof(CanInvoice), nameof(HasActions), nameof(RestorationText),
                     nameof(MaterialText), nameof(ShadeText), nameof(NotesText), nameof(ReceivedText), nameof(ExpectedText),
                     nameof(DeliveredText), nameof(TeethText), nameof(SubtotalText), nameof(UnitPriceText),
                     nameof(DiscountText), nameof(TotalText), nameof(QuantityText)
                 })
            OnPropertyChanged(n);
    }

    [RelayCommand] private void PrintWorkOrder() { if (Details is not null) _printWorkOrder(Details.Id); }

    [RelayCommand] private void Back() => _close();

    [RelayCommand]
    private async Task EditAsync()
    {
        if (Details is null) return;
        if (await _edit(Details)) await LoadAsync(Details.Id);
    }

    [RelayCommand]
    private async Task CreateInvoiceAsync()
    {
        if (Details is null) return;
        if (await _invoice(Details)) await LoadAsync(Details.Id);
    }

    [RelayCommand]
    private async Task ChangeStatusAsync(StatusActionItem? action)
    {
        if (Details is null || action is null) return;
        ErrorText = string.Empty;
        try
        {
            await _service.ChangeStatusAsync(Details.Id, action.Target, NoteText);
            NoteText = string.Empty;
            await LoadAsync(Details.Id);
        }
        catch (ValidationException ex)
        {
            ErrorText = string.Join("\n", ex.Errors.Select(e => Loc.GetOrNull("Val_" + e.ErrorCode) ?? Loc["Error_Generic"]).Distinct());
        }
        catch (Exception ex) { _log.LogError(ex, "Status change failed"); ErrorText = Loc["Error_Generic"]; }
    }
}
