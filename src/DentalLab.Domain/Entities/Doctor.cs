namespace DentalLab.Domain.Entities;

public class Doctor
{
    public int Id { get; private set; }
    /// <summary>Human-friendly ID, derived from the key (not stored).</summary>
    public string Code => $"D-{Id:0000}";
    public string FullName { get; private set; } = string.Empty;
    public string? ClinicName { get; private set; }
    public string? Phone { get; private set; }
    public string? Email { get; private set; }
    public string? Address { get; private set; }
    public string? Notes { get; private set; }
    public DateTime RegisteredUtc { get; private set; }
    /// <summary>Informational copy. The real balance is always the sum of ledger entries.</summary>
    public decimal OpeningBalance { get; private set; }
    public bool IsActive { get; private set; } = true;
    public List<LedgerEntry> LedgerEntries { get; } = new();

    private Doctor() { }

    public static Doctor Create(string fullName, string? clinicName, string? phone, string? email,
        string? address, string? notes, decimal openingBalance, bool isActive, DateTime? utcNow = null)
    {
        if (string.IsNullOrWhiteSpace(fullName))
            throw new ArgumentException("Doctor name is required.", nameof(fullName));

        var now = utcNow ?? DateTime.UtcNow;
        var doctor = new Doctor
        {
            RegisteredUtc = now,
            OpeningBalance = Money.Round(openingBalance),
            IsActive = isActive
        };
        doctor.Apply(fullName, clinicName, phone, email, address, notes);

        if (doctor.OpeningBalance != 0)
            doctor.LedgerEntries.Add(LedgerEntry.Opening(doctor.OpeningBalance, now));
        return doctor;
    }

    public void UpdateDetails(string fullName, string? clinicName, string? phone, string? email,
        string? address, string? notes, bool isActive)
    {
        if (string.IsNullOrWhiteSpace(fullName))
            throw new ArgumentException("Doctor name is required.", nameof(fullName));
        Apply(fullName, clinicName, phone, email, address, notes);
        IsActive = isActive;
    }

    public void SetActive(bool active) => IsActive = active;

    private void Apply(string fullName, string? clinic, string? phone, string? email, string? address, string? notes)
    {
        FullName = fullName.Trim();
        ClinicName = Clean(clinic);
        Phone = Clean(phone);
        Email = Clean(email);
        Address = Clean(address);
        Notes = Clean(notes);
    }

    private static string? Clean(string? s) => string.IsNullOrWhiteSpace(s) ? null : s.Trim();
}
