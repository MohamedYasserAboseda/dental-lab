using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Design;

namespace DentalLab.Infrastructure.Persistence;

/// <summary>Used only by `dotnet ef` to create migrations.</summary>
public sealed class DesignTimeDbContextFactory : IDesignTimeDbContextFactory<DentalLabDbContext>
{
    public DentalLabDbContext CreateDbContext(string[] args)
    {
        var options = new DbContextOptionsBuilder<DentalLabDbContext>()
            .UseSqlite("Data Source=design-time.db")
            .Options;
        return new DentalLabDbContext(options);
    }
}
