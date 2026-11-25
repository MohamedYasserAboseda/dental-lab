using DentalLab.Application;
using DentalLab.Application.Abstractions;
using DentalLab.Application.Documents;

namespace DentalLab.Infrastructure.Documents;

public sealed class LabSettingsService(ISettingsService settings, string brandingDirectory) : ILabSettingsService
{
    public const long MaxLogoBytes = 2 * 1024 * 1024;

    public async Task<LabSettings> GetAsync(CancellationToken ct = default)
    {
        async Task<string> S(string key, string def) => await settings.GetAsync(key, ct) ?? def;
        var d = LabSettings.Default;

        var logo = await settings.GetAsync(SettingKeys.LabLogoPath, ct);
        var profile = new LabProfile(
            await S(SettingKeys.LabName, d.Profile.Name), await S(SettingKeys.LabAddress, ""), await S(SettingKeys.LabPhone, ""),
            await S(SettingKeys.LabEmail, ""), string.IsNullOrWhiteSpace(logo) ? null : logo,
            await S(SettingKeys.LabCurrency, d.Profile.Currency));

        var format = Enum.TryParse<PrintFormat>(await settings.GetAsync(SettingKeys.PrintFormat, ct), out var f) && Enum.IsDefined(f) ? f : d.DefaultFormat;
        var lang = Enum.TryParse<DocLanguageMode>(await settings.GetAsync(SettingKeys.PrintLanguage, ct), out var l) && Enum.IsDefined(l) ? l : d.LanguageMode;
        return new LabSettings(profile, format, lang);
    }

    public async Task SaveAsync(LabSettings s, CancellationToken ct = default)
    {
        var p = s.Profile;
        await settings.SetAsync(SettingKeys.LabName, p.Name.Trim(), ct);
        await settings.SetAsync(SettingKeys.LabAddress, p.Address.Trim(), ct);
        await settings.SetAsync(SettingKeys.LabPhone, p.Phone.Trim(), ct);
        await settings.SetAsync(SettingKeys.LabEmail, p.Email.Trim(), ct);
        await settings.SetAsync(SettingKeys.LabCurrency, p.Currency.Trim(), ct);
        await settings.SetAsync(SettingKeys.LabLogoPath, p.LogoPath ?? "", ct);
        await settings.SetAsync(SettingKeys.PrintFormat, s.DefaultFormat.ToString(), ct);
        await settings.SetAsync(SettingKeys.PrintLanguage, s.LanguageMode.ToString(), ct);
    }

    public async Task<string> SaveLogoAsync(string sourcePath, CancellationToken ct = default)
    {
        var info = new FileInfo(sourcePath);
        if (!info.Exists || info.Length is 0 or > MaxLogoBytes) throw new InvalidDataException("LogoInvalid");

        var bytes = await File.ReadAllBytesAsync(sourcePath, ct);
        var ext = GetImageExtension(bytes) ?? throw new InvalidDataException("LogoInvalid");

        Directory.CreateDirectory(brandingDirectory);
        foreach (var old in Directory.EnumerateFiles(brandingDirectory, "logo.*")) File.Delete(old);
        var target = Path.Combine(brandingDirectory, "logo" + ext);
        await File.WriteAllBytesAsync(target, bytes, ct);
        return target;
    }

    /// <summary>Recognises PNG and JPEG by their signature, whatever the file is called.</summary>
    public static string? GetImageExtension(byte[] b)
    {
        if (b.Length > 8 && b[0] == 0x89 && b[1] == 0x50 && b[2] == 0x4E && b[3] == 0x47) return ".png";
        if (b.Length > 3 && b[0] == 0xFF && b[1] == 0xD8 && b[2] == 0xFF) return ".jpg";
        return null;
    }
}
