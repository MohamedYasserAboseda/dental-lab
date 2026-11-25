using System.Collections.ObjectModel;
using System.Diagnostics;
using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Documents;
using System.Windows.Markup;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using DentalLab.Application.Abstractions;
using DentalLab.Application.Documents;
using DentalLab.Desktop.Services;
using DentalLab.Shared;
using Microsoft.Extensions.Logging;

namespace DentalLab.Desktop.ViewModels;

public sealed partial class PrintPreviewViewModel : ObservableObject
{
    private readonly Func<DocumentRequest, Task<PrintableDocument>> _factory;
    private readonly ILabSettingsService _labs;
    private readonly FileDialogService _files;
    private readonly ILogger _log;
    private readonly string _titleKey;
    private PrintableDocument? _doc;
    private bool _loading;
    private int _version;

    public LocalizationService Loc => LocalizationService.Instance;
    public string Title => Loc[_titleKey];
    public ObservableCollection<OptionItem> FormatOptions { get; } = [];
    public ObservableCollection<OptionItem> LanguageOptions { get; } = [];
    public ObservableCollection<ImageSource> Pages { get; } = [];

    [ObservableProperty] private int _selectedFormatId = (int)PrintFormat.A4;
    [ObservableProperty] private int _selectedLanguageId = 1;
    [ObservableProperty] private bool _isBusy;
    [ObservableProperty] private string _statusText = string.Empty;
    [ObservableProperty] private bool _statusIsError;

    public bool HasStatus => !string.IsNullOrEmpty(StatusText);
    public bool CanUseDocument => _doc is not null && !IsBusy;
    /// <summary>Width of one page on screen: roughly proportional to the real paper.</summary>
    public double PageWidth => (PrintFormat)SelectedFormatId switch { PrintFormat.A5 => 540, PrintFormat.Thermal80 => 330, _ => 760 };

    public PrintPreviewViewModel(Func<DocumentRequest, Task<PrintableDocument>> factory, ILabSettingsService labs,
        FileDialogService files, ILogger log, string titleKey)
    {
        _factory = factory; _labs = labs; _files = files; _log = log; _titleKey = titleKey;
        foreach (var f in Enum.GetValues<PrintFormat>()) FormatOptions.Add(new OptionItem((int)f, Loc["Format_" + f]));
        LanguageOptions.Add(new OptionItem(1, Loc["DocLang_Arabic"]));
        LanguageOptions.Add(new OptionItem(2, Loc["DocLang_English"]));
    }

    public async Task LoadAsync()
    {
        _loading = true;
        try
        {
            var s = await _labs.GetAsync();
            SelectedFormatId = (int)s.DefaultFormat;
            var lang = s.LanguageMode switch
            {
                DocLanguageMode.Arabic => AppLanguage.Arabic,
                DocLanguageMode.English => AppLanguage.English,
                _ => Loc.Language
            };
            SelectedLanguageId = lang == AppLanguage.Arabic ? 1 : 2;
        }
        catch (Exception ex) { _log.LogError(ex, "Loading print defaults failed"); }
        finally { _loading = false; }
        await GenerateAsync();
    }

    partial void OnStatusTextChanged(string value) => OnPropertyChanged(nameof(HasStatus));
    partial void OnIsBusyChanged(bool value) => OnPropertyChanged(nameof(CanUseDocument));
    partial void OnSelectedFormatIdChanged(int value) { OnPropertyChanged(nameof(PageWidth)); if (!_loading) _ = GenerateAsync(); }
    partial void OnSelectedLanguageIdChanged(int value) { if (!_loading) _ = GenerateAsync(); }

    private async Task GenerateAsync()
    {
        var version = ++_version;
        IsBusy = true; StatusText = string.Empty;
        try
        {
            var request = new DocumentRequest((PrintFormat)SelectedFormatId, SelectedLanguageId == 1 ? AppLanguage.Arabic : AppLanguage.English);
            var doc = await _factory(request);
            if (version != _version) return;

            _doc = doc;
            Pages.Clear();
            foreach (var png in doc.Pages) Pages.Add(Decode(png));
        }
        catch (Exception ex)
        {
            _log.LogError(ex, "Creating the document failed");
            _doc = null; Pages.Clear();
            StatusText = Loc["Preview_Error"]; StatusIsError = true;
        }
        finally
        {
            if (version == _version) IsBusy = false;
            OnPropertyChanged(nameof(CanUseDocument));
        }
    }

    private static BitmapSource Decode(byte[] png)
    {
        var bmp = new BitmapImage();
        bmp.BeginInit();
        bmp.CacheOption = BitmapCacheOption.OnLoad;
        bmp.StreamSource = new MemoryStream(png);
        bmp.EndInit();
        bmp.Freeze();
        return bmp;
    }

    [RelayCommand]
    private void Print()
    {
        if (_doc is null) return;
        var dialog = new PrintDialog();
        if (dialog.ShowDialog() != true) return;

        try
        {
            var thermal = (PrintFormat)SelectedFormatId == PrintFormat.Thermal80;
            var document = new FixedDocument();
            document.DocumentPaginator.PageSize = new Size(dialog.PrintableAreaWidth, dialog.PrintableAreaHeight);

            foreach (var page in Pages.OfType<BitmapSource>())
            {
                double w = dialog.PrintableAreaWidth, h = dialog.PrintableAreaHeight;
                if (thermal) h = Math.Max(h, w * page.PixelHeight / page.PixelWidth); // roll paper: follow the length of the receipt
                var fixedPage = new FixedPage { Width = w, Height = h };
                fixedPage.Children.Add(new Image { Source = page, Stretch = Stretch.Uniform, Width = w, Height = h });
                var content = new PageContent();
                ((IAddChild)content).AddChild(fixedPage);
                document.Pages.Add(content);
            }
            dialog.PrintDocument(document.DocumentPaginator, _doc.FileName);
            StatusText = Loc["Preview_Printed"]; StatusIsError = false;
        }
        catch (Exception ex)
        {
            _log.LogError(ex, "Printing failed");
            StatusText = Loc["Preview_PrintError"]; StatusIsError = true;
        }
    }

    [RelayCommand]
    private void SavePdf()
    {
        if (_doc is null) return;
        var path = _files.PickSavePdf(_doc.FileName);
        if (path is null) return;
        try
        {
            File.WriteAllBytes(path, _doc.Pdf);
            StatusText = string.Format(Loc["Preview_Saved"], path); StatusIsError = false;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            _log.LogError(ex, "Saving the PDF failed");
            StatusText = Loc["Preview_SaveError"]; StatusIsError = true;
        }
    }

    [RelayCommand]
    private void OpenInViewer()
    {
        if (_doc is null) return;
        try
        {
            var path = Path.Combine(Path.GetTempPath(), _doc.FileName);
            File.WriteAllBytes(path, _doc.Pdf);
            Process.Start(new ProcessStartInfo(path) { UseShellExecute = true });
        }
        catch (Exception ex)
        {
            _log.LogError(ex, "Opening the PDF failed");
            StatusText = Loc["Preview_OpenError"]; StatusIsError = true;
        }
    }
}
