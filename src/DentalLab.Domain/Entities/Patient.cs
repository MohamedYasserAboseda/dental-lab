namespace DentalLab.Domain.Entities;

public class Patient
{
    public int Id { get; private set; }
    public string FullName { get; private set; } = string.Empty;
    public int DoctorId { get; private set; }
    public Doctor? Doctor { get; private set; }
    public string? Code { get; private set; }
    public string? Notes { get; private set; }
    public DateTime CreatedUtc { get; private set; }

    private Patient() { }

    public static Patient Create(string fullName, int doctorId, string? code, string? notes, DateTime? utcNow = null)
    {
        var p = new Patient { CreatedUtc = utcNow ?? DateTime.UtcNow };
        p.Apply(fullName, doctorId, code, notes);
        return p;
    }

    public void Update(string fullName, int doctorId, string? code, string? notes) => Apply(fullName, doctorId, code, notes);

    private void Apply(string fullName, int doctorId, string? code, string? notes)
    {
        if (string.IsNullOrWhiteSpace(fullName)) throw new ArgumentException("Patient name is required.", nameof(fullName));
        if (doctorId <= 0) throw new ArgumentException("A patient belongs to a doctor.", nameof(doctorId));
        FullName = fullName.Trim();
        DoctorId = doctorId;
        Code = string.IsNullOrWhiteSpace(code) ? null : code.Trim();
        Notes = string.IsNullOrWhiteSpace(notes) ? null : notes.Trim();
    }
}
