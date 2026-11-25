using DentalLab.Application.Catalog;

namespace DentalLab.Application.Abstractions;

public interface ICatalogService
{
    Task<IReadOnlyList<CatalogItem>> GetRestorationTypesAsync(bool activeOnly, CancellationToken ct = default);
    Task<IReadOnlyList<CatalogItem>> GetMaterialsAsync(bool activeOnly, CancellationToken ct = default);
    Task<int> SaveRestorationTypeAsync(int? id, CatalogItemInput input, CancellationToken ct = default);
    Task<int> SaveMaterialAsync(int? id, CatalogItemInput input, CancellationToken ct = default);

    /// <summary>Doctor-specific price if one exists, otherwise the restoration type's default price.</summary>
    Task<decimal> ResolveUnitPriceAsync(int doctorId, int restorationTypeId, CancellationToken ct = default);
    Task<IReadOnlyList<DoctorPriceItem>> GetDoctorPricesAsync(int doctorId, CancellationToken ct = default);
    /// <summary>A null price removes the doctor-specific price for that restoration type.</summary>
    Task SetDoctorPricesAsync(int doctorId, IReadOnlyDictionary<int, decimal?> prices, CancellationToken ct = default);
}
