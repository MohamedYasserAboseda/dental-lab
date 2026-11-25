using DentalLab.Shared;

namespace DentalLab.Application.Documents;

public enum PrintFormat { A4 = 1, A5 = 2, Thermal80 = 3 }
public enum DocLanguageMode { FollowApp = 0, Arabic = 1, English = 2 }
public enum PrintKind { Invoice, Receipt, Statement, WorkOrder }

public sealed record LabProfile(string Name, string Address, string Phone, string Email, string? LogoPath, string Currency);
public sealed record LabSettings(LabProfile Profile, PrintFormat DefaultFormat, DocLanguageMode LanguageMode)
{
    public static LabSettings Default => new(new LabProfile("", "", "", "", null, "EGP"), PrintFormat.A4, DocLanguageMode.FollowApp);
}

public sealed record DocumentRequest(PrintFormat Format, AppLanguage Language);

/// <summary>A finished document: the PDF itself and one PNG per page for the on-screen preview / printing.</summary>
public sealed record PrintableDocument(string FileName, byte[] Pdf, IReadOnlyList<byte[]> Pages);
