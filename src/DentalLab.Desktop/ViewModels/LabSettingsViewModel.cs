using System.Collections.ObjectModel;
using System.IO;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using DentalLab.Application.Abstractions;
using DentalLab.Application.Documents;
using DentalLab.Desktop.Services;
using Microsoft.Extensions.Logging;

namespace DentalLab.Desktop.ViewModels;

public sealed partial class LabSettingsViewModel : ObservableObject
{
    private readonly ILabSettingsService _labs;
    private readonly FileDialogService _files;
    private readonly ILogger _log;

    public LocalizationService Loc => LocalizationService.Instance;
    public ObservableCollection<OptionItem> FormatOptions { get; } = [];
    public ObservableCollection<OptionItem> LanguageOptions { get; } = [];

    [ObservableProperty] private string _name = string.Empty;
    [ObservableProperty] private string _address = string.Empty;
    [ObservableProperty] private string _phone = string.Empty;
    [ObservableProperty] private string _email = string.Empty;
    [ObservableProperty] private string _currency = "EGP";
    [ObservableProperty] private string? _logoPath;
    [ObservableProperty] private ImageSource? _logoImage;
    [ObservableProperty] private int _formatId = (int)PrintFormat.A4;
    [ObservableProperty] private int _languageMode;
    [ObservableProperty] private string _statusText = string.Empty;
    [ObservableProperty] private bool _statusIsError;

    public bool HasLogo => !string.IsNullOrEmpty(LogoPath);
    public bool HasStatus => !string.IsNullOrEmpty(StatusText);

    public LabSettingsViewModel(ILabSettingsService labs, FileDialogService files, ILogger log)
    {
        _labs = labs; _files = files; _log = log;
        foreach (var f in Enum.GetValues<PrintFormat>()) FormatOptions.Add(new OptionItem((int)f, Loc["Format_" + f]));
        LanguageOptions.Add(new OptionItem((int)DocLanguageMode.FollowApp, Loc["DocLang_Follow"]));
        LanguageOptions.Add(new OptionItem((int)DocLanguageMode.Arabic, Loc["DocLang_Arabic"]));
        LanguageOptions.Add(new OptionItem((int)DocLanguageMode.English, Loc["DocLang_English"]));
    }

    partial void OnStatusTextChanged(string value) => OnPropertyChanged(nameof(HasStatus));
    partial void OnLogoPathChanged(string? value) => OnPropertyChanged(nameof(HasLogo));

    public async Task InitializeAsync()
    {
        try
        {
            var s = await _labs.GetAsync();
            Name = s.Profile.Name; Address = s.Profile.Address; Phone = s.Profile.Phone; Email = s.Profile.Email;
            Currency = s.Profile.Currency; LogoPath = s.Profile.LogoPath; LogoImage = LoadPreview(s.Profile.LogoPath);
            FormatId = (int)s.DefaultFormat; LanguageMode = (int)s.LanguageMode;
        }
        catch (Exception ex) { _log.LogError(ex, "Loading the laboratory settings failed"); }
    }

    private static ImageSource? LoadPreview(string? path)
    {
        if (string.IsNullOrWhiteSpace(path) || !File.Exists(path)) return null;
        try
        {
            var bmp = new BitmapImage();
            bmp.BeginInit();
            bmp.CacheOption = BitmapCacheOption.OnLoad; // do not keep the file locked
            bmp.UriSource = new Uri(path);
            bmp.EndInit();
            bmp.Freeze();
            return bmp;
        }
        catch (Exception) { return null; }
    }

    private LabSettings Snapshot()
        => new(new LabProfile(Name, Address, Phone, Email, LogoPath, Currency), (PrintFormat)FormatId, (DocLanguageMode)LanguageMode);

    [RelayCommand]
    private async Task SaveAsync()
    {
        try { await _labs.SaveAsync(Snapshot()); StatusText = Loc["Lab_Saved"]; StatusIsError = false; }
        catch (Exception ex) { _log.LogError(ex, "Saving the laboratory settings failed"); StatusText = Loc["Error_Generic"]; StatusIsError = true; }
    }

    [RelayCommand]
    private async Task ChooseLogoAsync()
    {
        var picked = _files.PickImage();
        if (picked is null) return;
        try
        {
            LogoPath = await _labs.SaveLogoAsync(picked);
            LogoImage = LoadPreview(LogoPath);
            await SaveAsync();
        }
        catch (InvalidDataException) { StatusText = Loc["Val_LogoInvalid"]; StatusIsError = true; }
        catch (Exception ex) { _log.LogError(ex, "Saving the logo failed"); StatusText = Loc["Error_Generic"]; StatusIsError = true; }
    }

    [RelayCommand]
    private async Task ClearLogoAsync()
    {
        LogoPath = null; LogoImage = null;
        await SaveAsync();
    }
}
