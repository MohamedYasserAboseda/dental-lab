using System.ComponentModel;
using System.Globalization;
using System.Resources;
using System.Windows;
using DentalLab.Shared;

namespace DentalLab.Desktop.Services;

/// <summary>
/// Runtime language switching. XAML binds like:
///   {Binding [Nav_Dashboard], Source={x:Static services:LocalizationService.Instance}}
/// Raising PropertyChanged("") refreshes every binding at once, including FlowDirection (RTL/LTR).
/// </summary>
public sealed class LocalizationService : INotifyPropertyChanged
{
    public static LocalizationService Instance { get; } = new();

    private readonly ResourceManager _rm = new("DentalLab.Shared.Resources.Strings", typeof(AppPaths).Assembly);

    public AppLanguage Language { get; private set; } = AppLanguage.Arabic;
    public CultureInfo Culture { get; private set; } = new("ar");

    public string this[string key] => _rm.GetString(key, Culture) ?? $"[{key}]";
    public string? GetOrNull(string key) => _rm.GetString(key, Culture);
    public FlowDirection FlowDirection => Language == AppLanguage.Arabic ? FlowDirection.RightToLeft : FlowDirection.LeftToRight;
    public string LanguageToggleLabel => Language == AppLanguage.Arabic ? "EN" : "ع";

    public void SetLanguage(AppLanguage language)
    {
        Language = language;
        Culture = new CultureInfo(language == AppLanguage.Arabic ? "ar" : "en");
        CultureInfo.DefaultThreadCurrentCulture = Culture;
        CultureInfo.DefaultThreadCurrentUICulture = Culture;
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(string.Empty));
    }

    public event PropertyChangedEventHandler? PropertyChanged;
}
