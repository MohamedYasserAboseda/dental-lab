using System.Collections.ObjectModel;
using System.Globalization;
using System.Windows.Media;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using DentalLab.Application.Abstractions;
using DentalLab.Application.Dashboard;
using DentalLab.Desktop.Controls;
using DentalLab.Desktop.Services;
using DentalLab.Domain.Entities;
using Microsoft.Extensions.Logging;

namespace DentalLab.Desktop.ViewModels;

public sealed record StatCard(string Label, string Glyph, double Value, int Decimals, Brush Accent, string? Hint);
public sealed record BarRow(string Label, string ValueText, double Fraction, Brush Brush);
public sealed record ListRow(string Primary, string Secondary, string Right, Brush? RightBrush)
{
    public bool HasRightBrush => RightBrush is not null;
}
public sealed record RangeOption(string Key, string Text);

public sealed partial class DashboardViewModel : ObservableObject
{
    private static readonly CultureInfo Inv = CultureInfo.InvariantCulture;
    private static readonly string[] RangeKeys = ["ThisMonth", "Last30", "Last3Months", "Last6Months", "ThisYear", "AllTime"];

    private readonly IDashboardService _service;
    private readonly ILogger _log;
    private bool _building;
    private int _loadVersion;

    public LocalizationService Loc => LocalizationService.Instance;
    public ObservableCollection<StatCard> Cards { get; } = [];
    public ObservableCollection<BarRow> StatusRows { get; } = [];
    public ObservableCollection<BarRow> DoctorRows { get; } = [];
    public ObservableCollection<ListRow> PaymentRows { get; } = [];
    public ObservableCollection<ListRow> UpcomingRows { get; } = [];
    public ObservableCollection<ListRow> ActivityRows { get; } = [];
    public ObservableCollection<RangeOption> RangeOptions { get; } = [];

    [ObservableProperty] private RangeOption? _selectedRange;
    [ObservableProperty] private BarChartData? _revenueChart;
    [ObservableProperty] private BarChartData? _casesChart;
    [ObservableProperty] private bool _isLoading;

    public DashboardViewModel(IDashboardService service, ILoggerFactory loggers)
    {
        _service = service; _log = loggers.CreateLogger<DashboardViewModel>();
        BuildRangeOptions();
        Loc.PropertyChanged += (_, _) => { BuildRangeOptions(); _ = LoadAsync(); };
    }

    private void BuildRangeOptions()
    {
        _building = true;
        try
        {
            var key = SelectedRange?.Key ?? "ThisMonth";
            RangeOptions.Clear();
            foreach (var k in RangeKeys) RangeOptions.Add(new RangeOption(k, Loc["Range_" + k]));
            SelectedRange = RangeOptions.First(o => o.Key == key);
        }
        finally { _building = false; }
    }

    partial void OnSelectedRangeChanged(RangeOption? value) { if (!_building) _ = LoadAsync(); }

    public Task InitializeAsync() => LoadAsync();

    [RelayCommand] private Task RefreshAsync() => LoadAsync();

    private DashboardQuery BuildQuery()
    {
        var today = DateTime.Today;
        var tomorrow = today.AddDays(1);
        var monthStart = new DateTime(today.Year, today.Month, 1);
        return (SelectedRange?.Key ?? "ThisMonth") switch
        {
            "Last30" => new DashboardQuery(today.AddDays(-29), tomorrow),
            "Last3Months" => new DashboardQuery(monthStart.AddMonths(-2), tomorrow),
            "Last6Months" => new DashboardQuery(monthStart.AddMonths(-5), tomorrow),
            "ThisYear" => new DashboardQuery(new DateTime(today.Year, 1, 1), tomorrow),
            "AllTime" => new DashboardQuery(null, null),
            _ => new DashboardQuery(monthStart, tomorrow)
        };
    }

    private async Task LoadAsync()
    {
        var version = ++_loadVersion;
        IsLoading = true;
        try
        {
            var data = await _service.GetAsync(BuildQuery());
            if (version != _loadVersion) return;
            Build(data);
        }
        catch (Exception ex) { _log.LogError(ex, "Loading the dashboard failed"); }
        finally { if (version == _loadVersion) IsLoading = false; }
    }

    private static Brush B(string key) => (Brush)System.Windows.Application.Current.FindResource(key);

    private void Build(DashboardData d)
    {
        var gold = B("GoldBrush"); var success = B("SuccessBrush"); var warning = B("WarningBrush"); var danger = B("DangerBrush");

        Cards.Clear();
        void Add(string key, string glyph, double value, int decimals, Brush accent, string? hint = null)
            => Cards.Add(new StatCard(Loc[key], glyph, value, decimals, accent, hint));

        Add("Card_Doctors", "\uE77B", d.TotalDoctors, 0, gold);
        Add("Card_Cases", "\uE7C3", d.TotalCases, 0, gold);
        Add("Card_ReceivedToday", "\uE8A5", d.ReceivedToday, 0, gold);
        Add("Card_InProduction", "\uE895", d.InProduction, 0, warning);
        Add("Card_Ready", "\uE73E", d.ReadyForDelivery, 0, success);
        Add("Card_Overdue", "\uE7BA", d.Overdue, 0, danger);
        Add("Card_BilledToday", "\uE8C7", (double)d.BilledToday, 2, success);
        Add("Card_CollectedToday", "\uE8C7", (double)d.CollectedToday, 2, success);
        Add("Card_BilledMonth", "\uE8C7", (double)d.BilledThisMonth, 2, gold);
        Add("Card_CollectedMonth", "\uE8C7", (double)d.CollectedThisMonth, 2, gold);
        Add("Card_Outstanding", "\uE825", (double)d.OutstandingBalances, 2, warning);
        Add("Card_BilledRange", "\uE9D2", (double)d.BilledInRange, 2, gold,
            $"{Loc["Chart_Collected"]}: {d.CollectedInRange.ToString("N2", Inv)}");

        string Month(int y, int m) => new DateTime(y, m, 1).ToString("MMM", Loc.Culture);
        var labels = d.MonthlyBilled.Select(m => Month(m.Year, m.Month)).ToList();
        RevenueChart = new BarChartData(labels,
        [
            new ChartSeries(Loc["Chart_Billed"], "GoldBrush", d.MonthlyBilled.Select(m => (double)m.Value).ToList()),
            new ChartSeries(Loc["Chart_Collected"], "SuccessBrush", d.MonthlyCollected.Select(m => (double)m.Value).ToList())
        ]);
        CasesChart = new BarChartData(d.MonthlyCases.Select(m => Month(m.Year, m.Month)).ToList(),
            [new ChartSeries(Loc["Chart_CasesSeries"], "GoldBrush", d.MonthlyCases.Select(m => (double)m.Count).ToList())]);

        StatusRows.Clear();
        var totalInRange = Math.Max(1, d.StatusDistribution.Sum(s => s.Count));
        foreach (var s in d.StatusDistribution.Where(s => s.Count > 0))
            StatusRows.Add(new BarRow(Loc["Status_" + s.Status], s.Count.ToString(Inv), s.Count / (double)totalInRange, StatusBrush(s.Status)));

        DoctorRows.Clear();
        var topMax = Math.Max(1, d.TopDoctors.Select(t => t.Count).DefaultIfEmpty(1).Max());
        foreach (var t in d.TopDoctors) DoctorRows.Add(new BarRow(t.Name, t.Count.ToString(Inv), t.Count / (double)topMax, gold));

        PaymentRows.Clear();
        foreach (var p in d.RecentPayments)
            PaymentRows.Add(new ListRow(p.DoctorName, $"{p.Receipt}  ·  {p.Date.ToString("yyyy-MM-dd", Inv)}",
                p.Amount.ToString("N2", Inv), p.IsReversed ? danger : success));

        UpcomingRows.Clear();
        foreach (var u in d.UpcomingDeliveries)
            UpcomingRows.Add(new ListRow($"{u.CaseNumber}  ·  {u.PatientName}", u.DoctorName,
                u.Expected.ToString("yyyy-MM-dd", Inv), u.IsOverdue ? danger : null));

        ActivityRows.Clear();
        foreach (var a in d.RecentActivity)
            ActivityRows.Add(new ListRow(a.CaseNumber, Loc["Status_" + a.To],
                DateTime.SpecifyKind(a.ChangedUtc, DateTimeKind.Utc).ToLocalTime().ToString("MM-dd HH:mm", Inv), null));
    }

    private static Brush StatusBrush(CaseStatus s) => B(s switch
    {
        CaseStatus.Cancelled => "DangerBrush",
        CaseStatus.Delivered or CaseStatus.Ready => "SuccessBrush",
        CaseStatus.Received => "WarningBrush",
        _ => "GoldBrush"
    });
}
