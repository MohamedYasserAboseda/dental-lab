using CommunityToolkit.Mvvm.ComponentModel;
using DentalLab.Desktop.Services;

namespace DentalLab.Desktop.ViewModels;

public sealed class NavItemViewModel : ObservableObject
{
    public string Key { get; }
    public string Glyph { get; }
    private readonly string _locKey;

    public string Title => LocalizationService.Instance[_locKey];

    public NavItemViewModel(string key, string glyph, string locKey)
    {
        Key = key; Glyph = glyph; _locKey = locKey;
        LocalizationService.Instance.PropertyChanged += (_, _) => OnPropertyChanged(nameof(Title));
    }
}
