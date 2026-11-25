namespace DentalLab.Shared;

/// <summary>Single source of truth for every folder the app uses. Never inside the install directory.</summary>
public static class AppPaths
{
    public static string Root { get; } = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "DentalLab");

    public static string DatabaseDirectory => Path.Combine(Root, "Database");
    public static string DatabaseFile => Path.Combine(DatabaseDirectory, "dentallab.db");
    public static string CaseFiles => Path.Combine(Root, "Files", "Cases");
    public static string Invoices => Path.Combine(Root, "Documents", "Invoices");
    public static string Receipts => Path.Combine(Root, "Documents", "Receipts");
    public static string Backups => Path.Combine(Root, "Backups");
    public static string Logs => Path.Combine(Root, "Logs");
    public static string Branding => Path.Combine(Root, "Files", "Branding");

    public static void EnsureCreated()
    {
        foreach (var dir in new[] { DatabaseDirectory, CaseFiles, Invoices, Receipts, Backups, Logs, Branding })
            Directory.CreateDirectory(dir);
    }
}
