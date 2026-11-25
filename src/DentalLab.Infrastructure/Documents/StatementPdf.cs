using DentalLab.Application.Doctors;
using DentalLab.Domain.Services;
using QuestPDF.Fluent;
using QuestPDF.Infrastructure;
using static DentalLab.Infrastructure.Documents.PdfCommon;

namespace DentalLab.Infrastructure.Documents;

internal static class StatementPdf
{
    public static Document Build(DoctorDetails doctor, IReadOnlyList<StatementLine> lines, DocContext c)
        => Document.Create(d => d.Page(page =>
        {
            ConfigurePage(page, c);
            page.Header().Element(h => Header(h, c, c.T["Doc_Statement"], doctor.Code, $"{c.T["Doc_AsOf"]} {c.Date(DateTime.Today)}"));
            page.Content().PaddingTop(8).Column(col =>
            {
                col.Spacing(10);
                col.Item().Column(k =>
                {
                    k.Item().Text(doctor.FullName).Bold().FontSize(c.BaseSize + 3);
                    if (!string.IsNullOrWhiteSpace(doctor.ClinicName)) k.Item().Text(doctor.ClinicName);
                    var contact = string.Join("  •  ", new[] { doctor.Phone, doctor.Address }.Where(x => !string.IsNullOrWhiteSpace(x)));
                    if (contact.Length > 0) k.Item().Text(contact).FontColor(Muted);
                });

                col.Item().Element(e => { if (c.Compact) Compact(e, c, lines); else Table(e, c, lines); });

                col.Item().Background(Soft).Border(1).BorderColor(Gold).Padding(8).Row(r =>
                {
                    r.RelativeItem().Text(c.T["Doc_ClosingBalance"]).SemiBold();
                    r.AutoItem().Text(c.Money(doctor.Balance)).Bold().FontSize(c.BaseSize + 3).FontColor(doctor.Balance > 0 ? Danger : Navy);
                });
                col.Item().Text(c.T["Profile_BalanceHint"]).FontSize(c.BaseSize - 1).FontColor(Muted);
            });
            Footer(page, c);
        }));

    private static string Type(DocContext c, StatementLine l) => c.T["Ledger_" + l.Type];

    private static void Table(IContainer e, DocContext c, IReadOnlyList<StatementLine> lines)
        => e.Table(t =>
        {
            t.ColumnsDefinition(cd =>
            {
                cd.ConstantColumn(66); cd.RelativeColumn(1.4f); cd.RelativeColumn(2.2f);
                cd.RelativeColumn(1.1f); cd.RelativeColumn(1.1f); cd.RelativeColumn(1.3f);
            });
            t.Header(h =>
            {
                h.Cell().Element(HeaderCell).Text(c.T["Col_Date"]);
                h.Cell().Element(HeaderCell).Text(c.T["Col_Type"]);
                h.Cell().Element(HeaderCell).Text(c.T["Col_Description"]);
                h.Cell().Element(HeaderCell).Text(c.T["Col_Debit"]).AlignEnd();
                h.Cell().Element(HeaderCell).Text(c.T["Col_Credit"]).AlignEnd();
                h.Cell().Element(HeaderCell).Text(c.T["Col_RunningBalance"]).AlignEnd();
            });
            foreach (var l in lines)
            {
                t.Cell().Element(BodyCell).Text(c.Date(l.DateUtc.ToLocalTime()));
                t.Cell().Element(BodyCell).Text(Type(c, l));
                t.Cell().Element(BodyCell).Text(l.Description ?? "");
                t.Cell().Element(BodyCell).Text(l.Debit == 0 ? "" : c.Number(l.Debit)).AlignEnd();
                t.Cell().Element(BodyCell).Text(l.Credit == 0 ? "" : c.Number(l.Credit)).AlignEnd();
                t.Cell().Element(BodyCell).Text(c.Number(l.RunningBalance)).AlignEnd();
            }
        });

    private static void Compact(IContainer e, DocContext c, IReadOnlyList<StatementLine> lines)
        => e.Column(col =>
        {
            foreach (var l in lines)
                col.Item().BorderBottom(0.5f).BorderColor(Line).PaddingVertical(3).Column(x =>
                {
                    x.Item().Row(r =>
                    {
                        r.RelativeItem().Text($"{c.Date(l.DateUtc.ToLocalTime())}  {Type(c, l)}").FontColor(Muted);
                        r.AutoItem().Text((l.Debit > 0 ? l.Debit : -l.Credit).ToString("+0.00;-0.00", System.Globalization.CultureInfo.InvariantCulture));
                    });
                    if (!string.IsNullOrWhiteSpace(l.Description)) x.Item().Text(l.Description);
                    x.Item().Row(rr => { rr.RelativeItem(); rr.AutoItem().Text($"= {c.Number(l.RunningBalance)}").SemiBold(); });
                });
        });
}
