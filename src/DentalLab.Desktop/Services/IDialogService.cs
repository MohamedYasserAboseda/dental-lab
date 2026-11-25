using DentalLab.Application.Documents;
using DentalLab.Desktop.ViewModels;

namespace DentalLab.Desktop.Services;

public interface IDialogService
{
    /// <summary>Shows the modal doctor editor. Returns true when saved.</summary>
    bool? ShowDoctorEditor(DoctorEditorViewModel viewModel);

    /// <summary>Shows any of the shared-chrome dialogs. Returns true when saved.</summary>
    bool? ShowDialog(IDialogViewModel viewModel);

    /// <summary>Opens the print preview (paper size, language, print, save as PDF) for a stored record.</summary>
    void ShowPrintPreview(PrintKind kind, int id);
}
