using DentalLab.Domain.Entities;
using Xunit;

namespace DentalLab.Domain.Tests;

public class AuditLogTests
{
    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    public void Create_WithBlankAction_Throws(string action)
        => Assert.Throws<ArgumentException>(() => AuditLog.Create(action));

    [Fact]
    public void Create_TrimsActionAndUsesGivenTimestamp()
    {
        var now = new DateTime(2026, 1, 1, 10, 0, 0, DateTimeKind.Utc);
        var log = AuditLog.Create("  BackupCreated ", utcNow: now);
        Assert.Equal("BackupCreated", log.Action);
        Assert.Equal(now, log.TimestampUtc);
    }
}
