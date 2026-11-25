namespace DentalLab.Application.Abstractions;

public interface IDatabaseInitializer
{
    /// <summary>Creates folders, backs up before migrating, applies migrations, verifies integrity.</summary>
    Task InitializeAsync(CancellationToken ct = default);
}
