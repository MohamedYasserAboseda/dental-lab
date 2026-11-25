using CommunityToolkit.Mvvm.ComponentModel;
using DentalLab.Desktop.Services;
using FluentValidation;

namespace DentalLab.Desktop.ViewModels;

public interface IDialogViewModel
{
    string Title { get; }
    event Action<bool>? CloseRequested;
}

public sealed record OptionItem(int Id, string Name);

public abstract class DialogViewModelBase : ObservableObject, IDialogViewModel
{
    public LocalizationService Loc => LocalizationService.Instance;
    public abstract string Title { get; }
    public event Action<bool>? CloseRequested;

    protected void RequestClose(bool saved) => CloseRequested?.Invoke(saved);

    /// <summary>Maps validator error codes to localized "Val_&lt;code&gt;" texts; unknown codes fall back to a generic message.</summary>
    protected static string ErrorsToText(ValidationException ex)
    {
        var loc = LocalizationService.Instance;
        return string.Join("\n", ex.Errors.Select(e => loc.GetOrNull("Val_" + e.ErrorCode) ?? loc["Error_Generic"]).Distinct());
    }
}
