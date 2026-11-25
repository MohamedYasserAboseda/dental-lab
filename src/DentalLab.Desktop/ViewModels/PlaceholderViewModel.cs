namespace DentalLab.Desktop.ViewModels;

/// <summary>Shown for modules not built yet (clearly labelled, no fake data).</summary>
public sealed class PlaceholderViewModel(NavItemViewModel item)
{
    public NavItemViewModel Item { get; } = item;
}
