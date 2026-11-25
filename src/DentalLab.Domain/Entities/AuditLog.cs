namespace DentalLab.Domain.Entities;

public class AuditLog
{
    public long Id { get; private set; }
    public DateTime TimestampUtc { get; private set; }
    public string Action { get; private set; } = string.Empty;
    public string? EntityName { get; private set; }
    public string? EntityId { get; private set; }
    public string? Details { get; private set; }

    private AuditLog() { }

    public static AuditLog Create(string action, string? entityName = null, string? entityId = null,
        string? details = null, DateTime? utcNow = null)
    {
        if (string.IsNullOrWhiteSpace(action))
            throw new ArgumentException("Audit action is required.", nameof(action));

        return new AuditLog
        {
            TimestampUtc = utcNow ?? DateTime.UtcNow,
            Action = action.Trim(),
            EntityName = entityName,
            EntityId = entityId,
            Details = details
        };
    }
}
