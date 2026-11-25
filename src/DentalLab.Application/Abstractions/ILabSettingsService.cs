using DentalLab.Application.Documents;

namespace DentalLab.Application.Abstractions;

public interface ILabSettingsService
{
    Task<LabSettings> GetAsync(CancellationToken ct = default);
    Task SaveAsync(LabSettings settings, CancellationToken ct = default);
    /// <summary>Validates (PNG/JPG, at most 2 MB) and copies the image into the app's own folder. Returns the stored path.</summary>
    Task<string> SaveLogoAsync(string sourcePath, CancellationToken ct = default);
}
