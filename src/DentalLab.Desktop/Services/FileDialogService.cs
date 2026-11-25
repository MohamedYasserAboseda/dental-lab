using Microsoft.Win32;

namespace DentalLab.Desktop.Services;

public sealed class FileDialogService
{
    public string? PickImage()
    {
        var d = new OpenFileDialog { Filter = "Images (*.png;*.jpg;*.jpeg)|*.png;*.jpg;*.jpeg", CheckFileExists = true };
        return d.ShowDialog() == true ? d.FileName : null;
    }

    public string[] PickAttachments()
    {
        var d = new OpenFileDialog
        {
            Filter = "Case files (*.jpg;*.jpeg;*.png;*.stl;*.ply)|*.jpg;*.jpeg;*.png;*.stl;*.ply",
            Multiselect = true, CheckFileExists = true
        };
        return d.ShowDialog() == true ? d.FileNames : [];
    }

    public string? PickSavePdf(string suggestedName)
    {
        var d = new SaveFileDialog { Filter = "PDF (*.pdf)|*.pdf", FileName = suggestedName, DefaultExt = ".pdf", OverwritePrompt = true };
        return d.ShowDialog() == true ? d.FileName : null;
    }
}
