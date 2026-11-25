using DentalLab.Application.Cases;
using DentalLab.Domain.Dental;
using QuestPDF.Fluent;
using QuestPDF.Infrastructure;
using static DentalLab.Infrastructure.Documents.PdfCommon;

namespace DentalLab.Infrastructure.Documents;

/// <summary>The production copy of a case. It deliberately shows no prices.</summary>
internal static class WorkOrderPdf
{
    public static Document Build(CaseDetails cs, DocContext c)
        => Document.Create(d => d.Page(page =>
        {
            ConfigurePage(page, c);
            page.Header().Element(h => Header(h, c, c.T["Doc_WorkOrder"], cs.CaseNumber, c.Date(cs.ReceivedUtc.ToLocalTime())));
            page.Content().PaddingTop(8).Column(col =>
            {
                col.Spacing(10);
                var arabic = c.Rtl;
                string Pick(string name, string? ar) => arabic && !string.IsNullOrWhiteSpace(ar) ? ar! : name;

                col.Item().Column(k =>
                {
                    k.Spacing(3);
                    KeyValue(k, c, c.T["Field_Doctor"], cs.DoctorName);
                    KeyValue(k, c, c.T["Field_Patient"], cs.PatientName);
                    KeyValue(k, c, c.T["Field_Restoration"], Pick(cs.RestorationName, cs.RestorationNameAr));
                    KeyValue(k, c, c.T["Field_Material"], cs.MaterialName is null ? null : Pick(cs.MaterialName, cs.MaterialNameAr));
                    KeyValue(k, c, c.T["Field_Shade"], cs.Shade);
                    KeyValue(k, c, c.T["Field_Quantity"], cs.Quantity.ToString());
                    KeyValue(k, c, c.T["Details_Received"], c.DateTimeText(cs.ReceivedUtc.ToLocalTime()));
                    KeyValue(k, c, c.T["Col_Expected"], c.Date(cs.ExpectedDeliveryDate));
                });

                col.Item().Text(c.T["Col_Teeth"]).SemiBold().FontColor(Gold);
                var selected = cs.Teeth.ToHashSet();
                if (selected.Count == 0) col.Item().Text("—").FontColor(Muted);
                else
                {
                    col.Item().Text(string.Join(", ", cs.Teeth)).SemiBold();
                    if (!c.Compact)
                    {
                        var primary = selected.Any(n => Fdi.Get(n).Dentition == Dentition.Primary);
                        var permanent = selected.Any(n => Fdi.Get(n).Dentition == Dentition.Permanent);
                        // The chart is always drawn left to right, the way a dentist reads it (patient's right on the left).
                        if (permanent) col.Item().ContentFromLeftToRight().Column(g => Chart(g, c, selected, Dentition.Permanent));
                        if (primary) col.Item().ContentFromLeftToRight().Column(g => Chart(g, c, selected, Dentition.Primary));
                    }
                }

                if (!string.IsNullOrWhiteSpace(cs.Notes))
                {
                    col.Item().Text(c.T["Field_Notes"]).SemiBold().FontColor(Gold);
                    col.Item().Background(Soft).Padding(6).Text(cs.Notes);
                }

                col.Item().PaddingTop(6).Row(r =>
                {
                    r.RelativeItem().Column(s => Signature(s, c.T["Doc_Technician"]));
                    r.ConstantItem(c.Compact ? 12 : 30);
                    r.RelativeItem().Column(s => Signature(s, c.T["Doc_QC"]));
                });
            });
            Footer(page, c);
        }));

    private static void Chart(ColumnDescriptor g, DocContext c, HashSet<int> selected, Dentition d)
    {
        g.Spacing(3);
        g.Item().Element(e => ToothRow(e, selected, Fdi.ArchOrder(d, upper: true)));
        g.Item().Element(e => ToothRow(e, selected, Fdi.ArchOrder(d, upper: false)));
    }

    private static void ToothRow(IContainer e, HashSet<int> selected, IReadOnlyList<int> order)
        => e.Row(r =>
        {
            foreach (var n in order)
                r.RelativeItem().Padding(1).Border(0.7f).BorderColor("#B7C0CE")
                    .Background(selected.Contains(n) ? Gold : "#FFFFFF")
                    .PaddingVertical(5).AlignCenter().Text(n.ToString()).FontSize(8)
                    .FontColor(selected.Contains(n) ? Navy : Muted);
        });
}
