using DentalLab.Application.Billing;
using DentalLab.Application.Documents;
using DentalLab.Application.Doctors;
using DentalLab.Domain.Entities;
using QuestPDF.Fluent;
using QuestPDF.Helpers;
using QuestPDF.Infrastructure;
using static DentalLab.Infrastructure.Documents.PdfCommon;

namespace DentalLab.Infrastructure.Documents;

internal sealed record InvoiceLineView(string Description, int Quantity, decimal UnitPrice, decimal Discount, decimal Amount);

internal static class InvoicePdf
{
    public static Document Build(InvoiceDetails inv, DoctorDetails? doctor, IReadOnlyList<InvoiceLineView> lines, DocContext c)
        => Document.Create(d => d.Page(page =>
        {
            ConfigurePage(page, c);
            page.Header().Element(h => Header(h, c, c.T["Doc_Invoice"], inv.Number, c.Date(inv.DateUtc.ToLocalTime())));
            page.Content().PaddingTop(8).Column(col =>
            {
                col.Spacing(10);
                if (inv.Status == InvoiceStatus.Voided)
                    col.Item().Text(c.T["Doc_Voided"]).Bold().FontSize(c.BaseSize + 4).FontColor(Danger);

                col.Item().Element(e => DoctorBlock(e, c, inv.DoctorName, doctor));
                col.Item().Element(e => { if (c.Compact) CompactLines(e, c, lines); else LinesTable(e, c, lines); });
                col.Item().Element(e => Totals(e, c, inv));

                var applied = inv.Payments.Where(p => !p.IsReversed).ToList();
                if (applied.Count > 0)
                    col.Item().Column(p =>
                    {
                        p.Item().Text(c.T["Doc_AppliedPayments"]).SemiBold().FontColor(Gold);
                        foreach (var x in applied)
                            p.Item().Row(r =>
                            {
                                r.RelativeItem().Text($"{x.ReceiptNumber}  ·  {c.Date(x.Date)}  ·  {c.T["Method_" + x.Method]}").FontColor(Muted);
                                r.AutoItem().Text(c.Money(x.Amount));
                            });
                    });

                if (!string.IsNullOrWhiteSpace(inv.Description))
                    col.Item().Background(Soft).Padding(6).Text(inv.Description).FontColor(Muted);

                col.Item().PaddingTop(6).AlignCenter().Text(c.T["Doc_Thanks"]).FontColor(Muted);
            });
            Footer(page, c);
        }));

    private static void DoctorBlock(IContainer e, DocContext c, string name, DoctorDetails? d)
        => e.Column(col =>
        {
            col.Item().Text(c.T["Doc_BillTo"]).FontColor(Muted).FontSize(c.BaseSize - 1);
            col.Item().Text(name).Bold().FontSize(c.BaseSize + 2);
            if (d is not null)
            {
                if (!string.IsNullOrWhiteSpace(d.ClinicName)) col.Item().Text(d.ClinicName);
                var contact = string.Join("  •  ", new[] { d.Phone, d.Address }.Where(x => !string.IsNullOrWhiteSpace(x)));
                if (contact.Length > 0) col.Item().Text(contact).FontColor(Muted);
            }
        });

    private static void LinesTable(IContainer e, DocContext c, IReadOnlyList<InvoiceLineView> lines)
        => e.Table(t =>
        {
            t.ColumnsDefinition(cd =>
            {
                cd.ConstantColumn(22); cd.RelativeColumn(4); cd.ConstantColumn(34);
                cd.RelativeColumn(1.3f); cd.RelativeColumn(1.2f); cd.RelativeColumn(1.4f);
            });
            t.Header(h =>
            {
                h.Cell().Element(HeaderCell).Text("#");
                h.Cell().Element(HeaderCell).Text(c.T["Doc_Item"]);
                h.Cell().Element(HeaderCell).Text(c.T["Field_Quantity"]).AlignEnd();
                h.Cell().Element(HeaderCell).Text(c.T["Field_UnitPrice"]).AlignEnd();
                h.Cell().Element(HeaderCell).Text(c.T["Field_Discount"]).AlignEnd();
                h.Cell().Element(HeaderCell).Text(c.T["Col_Amount"]).AlignEnd();
            });
            var i = 1;
            foreach (var l in lines)
            {
                t.Cell().Element(BodyCell).Text((i++).ToString());
                t.Cell().Element(BodyCell).Text(l.Description);
                t.Cell().Element(BodyCell).Text(l.Quantity.ToString()).AlignEnd();
                t.Cell().Element(BodyCell).Text(c.Number(l.UnitPrice)).AlignEnd();
                t.Cell().Element(BodyCell).Text(l.Discount == 0 ? "" : c.Number(l.Discount)).AlignEnd();
                t.Cell().Element(BodyCell).Text(c.Number(l.Amount)).AlignEnd();
            }
        });

    private static void CompactLines(IContainer e, DocContext c, IReadOnlyList<InvoiceLineView> lines)
        => e.Column(col =>
        {
            foreach (var l in lines)
                col.Item().BorderBottom(0.5f).BorderColor(Line).PaddingVertical(3).Column(x =>
                {
                    x.Item().Text(l.Description);
                    x.Item().Row(r =>
                    {
                        r.RelativeItem().Text($"{l.Quantity} × {c.Number(l.UnitPrice)}").FontColor(Muted);
                        r.AutoItem().Text(c.Number(l.Amount)).SemiBold();
                    });
                });
        });

    private static void Totals(IContainer e, DocContext c, InvoiceDetails inv)
    {
        void Rows(ColumnDescriptor col)
        {
            void Row(string label, decimal value, bool strong = false, string? color = null)
                => col.Item().PaddingVertical(1.5f).Row(r =>
                {
                    r.RelativeItem().Text(label).FontColor(strong ? Navy : Muted);
                    var t = r.AutoItem().Text(c.Money(value));
                    if (strong) t.Bold().FontSize(c.BaseSize + 2);
                    if (color is not null) t.FontColor(color);
                });

            Row(c.T["Details_Subtotal"], inv.Subtotal);
            if (inv.Discount > 0) Row(c.T["Field_Discount"], -inv.Discount);
            Row(c.T["Field_Total"], inv.Total, strong: true, color: Gold);
            Row(c.T["Col_Paid"], inv.Paid);
            Row(c.T["Col_Remaining"], inv.Remaining, strong: true);
        }

        if (c.Compact) e.Column(Rows);
        else e.Row(r => { r.RelativeItem(); r.ConstantItem(c.Format == PrintFormat.A5 ? 190 : 220).Column(Rows); });
    }
}
