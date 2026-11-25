using System.Windows;
using DentalLab.Shared;

namespace DentalLab.Desktop.Services;

public sealed class ThemeService
{
    public AppTheme Current { get; private set; } = AppTheme.Light;

    public void Apply(AppTheme theme)
    {
        var dicts = System.Windows.Application.Current.Resources.MergedDictionaries;
        var index = -1;
        for (var i = 0; i < dicts.Count; i++)
        {
            if (dicts[i].Source?.OriginalString.Contains("Themes/Colors.", StringComparison.OrdinalIgnoreCase) == true)
            { index = i; break; }
        }

        var next = new ResourceDictionary { Source = new Uri($"pack://application:,,,/Themes/Colors.{theme}.xaml", UriKind.Absolute) };
        if (index >= 0) dicts[index] = next; else dicts.Insert(0, next);
        Current = theme;
    }

    public AppTheme Toggle()
    {
        Apply(Current == AppTheme.Light ? AppTheme.Dark : AppTheme.Light);
        return Current;
    }
}
