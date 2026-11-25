using DentalLab.Domain.Dental;

namespace DentalLab.Domain.Entities;

public class CaseTooth
{
    public int Id { get; private set; }
    public int CaseId { get; private set; }
    public int ToothNumber { get; private set; }
    private CaseTooth() { }
    public CaseTooth(int toothNumber) => ToothNumber = toothNumber;
}

public class CaseStatusHistory
{
    public int Id { get; private set; }
    public int CaseId { get; private set; }
    public CaseStatus? FromStatus { get; private set; }
    public CaseStatus ToStatus { get; private set; }
    public DateTime ChangedUtc { get; private set; }
    public string? Note { get; private set; }
    /// <summary>Single-user release: always the local operator. Becomes the real user when accounts are added.</summary>
    public string ChangedBy { get; private set; } = "Admin";
    private CaseStatusHistory() { }
    public CaseStatusHistory(CaseStatus? from, CaseStatus to, DateTime utc, string? note)
    { FromStatus = from; ToStatus = to; ChangedUtc = utc; Note = string.IsNullOrWhiteSpace(note) ? null : note.Trim(); }
}

public class LabCase
{
    public int Id { get; private set; }
    public string CaseNumber { get; private set; } = string.Empty;
    public int DoctorId { get; private set; }
    public Doctor? Doctor { get; private set; }
    public int PatientId { get; private set; }
    public Patient? Patient { get; private set; }
    public DateTime ReceivedUtc { get; private set; }
    public DateTime ExpectedDeliveryDate { get; private set; }
    public DateTime? ActualDeliveryUtc { get; private set; }
    public int RestorationTypeId { get; private set; }
    public RestorationType? RestorationType { get; private set; }
    public int? MaterialId { get; private set; }
    public Material? Material { get; private set; }
    public string? Shade { get; private set; }
    public int Quantity { get; private set; }
    /// <summary>Snapshot taken when the case was saved: later price-list changes never alter it.</summary>
    public decimal UnitPrice { get; private set; }
    public decimal Discount { get; private set; }
    public decimal Total { get; private set; }
    public string? Notes { get; private set; }
    public CaseStatus Status { get; private set; }
    /// <summary>Set once the case is billed. A billed case cannot be invoiced again, edited, or cancelled.</summary>
    public int? InvoiceId { get; private set; }
    public List<CaseTooth> Teeth { get; } = new();
    public List<CaseStatusHistory> History { get; } = new();

    private LabCase() { }

    public bool IsEditable => !CaseWorkflow.IsFinal(Status);
    public bool IsInvoiced => InvoiceId is not null;

    public void AssignInvoice(int invoiceId)
    {
        if (IsInvoiced) throw new InvalidOperationException("Case is already invoiced.");
        if (Status == CaseStatus.Cancelled) throw new InvalidOperationException("A cancelled case cannot be invoiced.");
        InvoiceId = invoiceId;
    }

    public void ReleaseInvoice() => InvoiceId = null;
    public bool IsOverdue(DateTime today) => IsEditable && ExpectedDeliveryDate.Date < today.Date;

    public static decimal CalculateTotal(int quantity, decimal unitPrice, decimal discount)
        => Money.Round(quantity * Money.Round(unitPrice) - Money.Round(discount));

    public static LabCase Create(string caseNumber, int doctorId, int patientId, int restorationTypeId, int? materialId,
        string? shade, int quantity, decimal unitPrice, decimal discount, DateTime expectedDelivery, string? notes,
        IEnumerable<int> teeth, DateTime utcNow)
    {
        if (string.IsNullOrWhiteSpace(caseNumber)) throw new ArgumentException("Case number is required.", nameof(caseNumber));
        var c = new LabCase { CaseNumber = caseNumber, ReceivedUtc = utcNow, Status = CaseStatus.Received };
        c.Apply(doctorId, patientId, restorationTypeId, materialId, shade, quantity, unitPrice, discount, expectedDelivery, notes, teeth);
        c.History.Add(new CaseStatusHistory(null, CaseStatus.Received, utcNow, null));
        return c;
    }

    public void UpdateDetails(int doctorId, int patientId, int restorationTypeId, int? materialId, string? shade,
        int quantity, decimal unitPrice, decimal discount, DateTime expectedDelivery, string? notes, IEnumerable<int> teeth)
    {
        if (!IsEditable) throw new InvalidOperationException("A delivered or cancelled case cannot be edited.");
        if (IsInvoiced) throw new InvalidOperationException("An invoiced case cannot be edited.");
        Apply(doctorId, patientId, restorationTypeId, materialId, shade, quantity, unitPrice, discount, expectedDelivery, notes, teeth);
    }

    public void ChangeStatus(CaseStatus to, string? note, DateTime utcNow)
    {
        if (!CaseWorkflow.CanTransition(Status, to))
            throw new InvalidOperationException($"Cannot move a case from {Status} to {to}.");
        if (to == CaseStatus.Cancelled && IsInvoiced) throw new InvalidOperationException("An invoiced case cannot be cancelled.");
        History.Add(new CaseStatusHistory(Status, to, utcNow, note));
        Status = to;
        if (to == CaseStatus.Delivered) ActualDeliveryUtc = utcNow;
    }

    private void Apply(int doctorId, int patientId, int restorationTypeId, int? materialId, string? shade, int quantity,
        decimal unitPrice, decimal discount, DateTime expectedDelivery, string? notes, IEnumerable<int> teeth)
    {
        if (doctorId <= 0) throw new ArgumentException("Doctor is required.", nameof(doctorId));
        if (patientId <= 0) throw new ArgumentException("Patient is required.", nameof(patientId));
        if (restorationTypeId <= 0) throw new ArgumentException("Restoration type is required.", nameof(restorationTypeId));
        if (quantity < 1) throw new ArgumentOutOfRangeException(nameof(quantity), "Quantity must be at least 1.");
        if (unitPrice < 0) throw new ArgumentOutOfRangeException(nameof(unitPrice), "Unit price cannot be negative.");
        if (discount < 0 || Money.Round(discount) > Money.Round(quantity * Money.Round(unitPrice)))
            throw new ArgumentOutOfRangeException(nameof(discount), "Discount must be between 0 and the subtotal.");

        var normalized = Fdi.Normalize(teeth); // validates FDI numbers

        DoctorId = doctorId; PatientId = patientId; RestorationTypeId = restorationTypeId; MaterialId = materialId;
        Shade = string.IsNullOrWhiteSpace(shade) ? null : shade.Trim();
        Quantity = quantity;
        UnitPrice = Money.Round(unitPrice);
        Discount = Money.Round(discount);
        Total = CalculateTotal(quantity, unitPrice, discount);
        ExpectedDeliveryDate = expectedDelivery.Date;
        Notes = string.IsNullOrWhiteSpace(notes) ? null : notes.Trim();

        Teeth.Clear();
        foreach (var n in normalized) Teeth.Add(new CaseTooth(n));
    }
}
