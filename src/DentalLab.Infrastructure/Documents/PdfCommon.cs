using QuestPDF.Fluent;
using QuestPDF.Helpers;
using QuestPDF.Infrastructure;
using DentalLab.Application.Documents;

namespace DentalLab.Infrastructure.Documents;

internal static class PdfCommon
{
    public const string Navy = "#101D35";
    public const string Gold = "#C6A15B";
    public const string Muted = "#6B7686";
    public const string Line = "#E3E8EF";
    public const string Soft = "#F5F7FA";
    public const string Danger = "#D9534F";

    public static void ConfigurePage(PageDescriptor page, DocContext c)
    {
        switch (c.Format)
        {
            case PrintFormat.A5: page.Size(PageSizes.A5); page.Margin(24); break;
            case PrintFormat.Thermal80: page.ContinuousSize(80, Unit.Millimetre); page.Margin(5, Unit.Millimetre); break;
            default: page.Size(PageSizes.A4); page.Margin(36); break;
        }
        page.PageColor(Colors.White);
        page.DefaultTextStyle(t => t.FontFamily("Segoe UI", "Tahoma", "Arial").FontSize(c.BaseSize).FontColor(Navy));
        if (c.Rtl) page.ContentFromRightToLeft(); else page.ContentFromLeftToRight();
    }

    /// <summary>Lab identity on one side, document title / number / date on the other (mirrored automatically for Arabic).</summary>
    public static void Header(IContainer container, DocContext c, string title, string number, string date)
    {
        container.PaddingBottom(6).Column(col =>
        {
            if (c.Compact)
            {
                col.Item().AlignCenter().Text(c.LabName).Bold().FontSize(c.BaseSize + 4);
                if (!string.IsNullOrWhiteSpace(c.Lab.Phone)) col.Item().AlignCenter().Text(c.Lab.Phone).FontColor(Muted);
                col.Item().PaddingTop(4).AlignCenter().Text(title).Bold().FontColor(Gold).FontSize(c.BaseSize + 2);
                col.Item().AlignCenter().Text(number).SemiBold();
                col.Item().AlignCenter().Text(date).FontColor(Muted);
            }
            else
            {
                col.Item().Row(row =>
                {
                    if (c.Logo is not null) row.ConstantItem(58).Height(58).Image(c.Logo).FitArea();
                    row.RelativeItem().PaddingHorizontal(c.Logo is not null ? 10 : 0).Column(l =>
                    {
                        l.Item().Text(c.LabName).FontSize(c.BaseSize + 6).Bold();
                        if (!string.IsNullOrWhiteSpace(c.Lab.Address)) l.Item().Text(c.Lab.Address).FontColor(Muted);
                        var contact = string.Join("  •  ", new[] { c.Lab.Phone, c.Lab.Email }.Where(x => !string.IsNullOrWhiteSpace(x)));
                        if (contact.Length > 0) l.Item().Text(contact).FontColor(Muted);
                    });
                    row.ConstantItem(c.Format == PrintFormat.A5 ? 130 : 170).Column(r =>
                    {
                        r.Item().Text(title).FontSize(c.BaseSize + 8).Bold().FontColor(Gold).AlignEnd();
                        r.Item().Text(number).SemiBold().AlignEnd();
                        r.Item().Text(date).FontColor(Muted).AlignEnd();
                    });
                });
            }
            col.Item().PaddingTop(6).LineHorizontal(1.2f).LineColor(Gold);
        });
    }

    public static void Footer(PageDescriptor page, DocContext c)
    {
        page.Footer().Column(col =>
        {
            col.Item().LineHorizontal(0.5f).LineColor(Line);
            col.Item().PaddingTop(3).Row(r =>
            {
                r.RelativeItem().Text($"{c.T["Doc_PrintedOn"]}: {c.DateTimeText(DateTime.Now)}").FontSize(c.BaseSize - 2).FontColor(Muted);
                if (!c.Compact)
                    r.AutoItem().Text(t =>
                    {
                        t.DefaultTextStyle(x => x.FontSize(c.BaseSize - 2).FontColor(Muted));
                        t.CurrentPageNumber(); t.Span(" / "); t.TotalPages();
                    });
            });
        });
    }

    public static IContainer HeaderCell(IContainer c)
        => c.Background(Navy).PaddingVertical(5).PaddingHorizontal(6).DefaultTextStyle(x => x.FontColor(Colors.White).SemiBold());

    public static IContainer BodyCell(IContainer c)
        => c.BorderBottom(0.5f).BorderColor(Line).PaddingVertical(5).PaddingHorizontal(6);

    public static void KeyValue(ColumnDescriptor col, DocContext c, string key, string? value)
    {
        if (string.IsNullOrWhiteSpace(value)) return;
        col.Item().Row(r =>
        {
            r.ConstantItem(c.Compact ? 62 : 92).Text(key).FontColor(Muted);
            r.RelativeItem().Text(value).SemiBold();
        });
    }

    public static void Signature(ColumnDescriptor col, string label)
    {
        col.Item().PaddingTop(26).Column(s =>
        {
            s.Item().LineHorizontal(0.6f).LineColor(Muted);
            s.Item().PaddingTop(2).Text(label).FontColor(Muted).FontSize(8);
        });
    }
}
