using DentalLab.Application.Billing;
using QuestPDF.Fluent;
using QuestPDF.Infrastructure;
using static DentalLab.Infrastructure.Documents.PdfCommon;

namespace DentalLab.Infrastructure.Documents;

internal static class ReceiptPdf
{
    public static Document Build(PaymentListItem p, DocContext c)
        => Document.Create(d => d.Page(page =>
        {
            ConfigurePage(page, c);
            page.Header().Element(h => Header(h, c, c.T["Doc_ReceiptTitle"], p.ReceiptNumber, c.Date(p.Date)));
            page.Content().PaddingTop(10).Column(col =>
            {
                col.Spacing(10);
                if (p.IsReversed) col.Item().Text(c.T["Invoice_Reversed"]).Bold().FontSize(c.BaseSize + 4).FontColor(Danger);

                col.Item().Border(1).BorderColor(Gold).Background(Soft).Padding(10).Column(a =>
                {
                    a.Item().AlignCenter().Text(c.T["Doc_AmountReceived"]).FontColor(Muted);
                    a.Item().AlignCenter().Text(c.Money(p.Amount)).Bold().FontSize(c.BaseSize + 10).FontColor(Navy);
                });

                col.Item().Column(k =>
                {
                    k.Spacing(4);
                    KeyValue(k, c, c.T["Field_Doctor"], p.DoctorName);
                    KeyValue(k, c, c.T["Col_Date"], c.Date(p.Date));
                    KeyValue(k, c, c.T["Field_Method"], c.T["Method_" + p.Method]);
                    KeyValue(k, c, c.T["Doc_RelatedInvoice"], p.InvoiceNumber ?? c.T["Doc_OnAccount"]);
                    KeyValue(k, c, c.T["Field_Notes"], p.Notes);
                });

                if (!c.Compact)
                    col.Item().PaddingTop(10).Row(r =>
                    {
                        r.RelativeItem().Column(s => Signature(s, c.T["Doc_ReceivedBy"]));
                        r.ConstantItem(40);
                        r.RelativeItem().Column(s => Signature(s, c.T["Doc_DoctorSignature"]));
                    });

                col.Item().PaddingTop(6).AlignCenter().Text(c.T["Doc_Thanks"]).FontColor(Muted);
            });
            Footer(page, c);
        }));
}
