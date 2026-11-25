using DentalLab.Application.Abstractions;
using DentalLab.Application.Documents;
using DentalLab.Desktop.ViewModels;
using DentalLab.Desktop.Views;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

namespace DentalLab.Desktop.Services;

public sealed class DialogService(IServiceProvider services) : IDialogService
{
    private static System.Windows.Window? Owner => System.Windows.Application.Current.MainWindow;

    public bool? ShowDoctorEditor(DoctorEditorViewModel viewModel)
        => new DoctorEditorWindow(viewModel) { Owner = Owner }.ShowDialog();

    public bool? ShowDialog(IDialogViewModel viewModel)
    {
        DialogWindowBase window = viewModel switch
        {
            PatientEditorViewModel p => new PatientEditorWindow(p),
            CatalogItemEditorViewModel c => new CatalogItemEditorWindow(c),
            DoctorPricesViewModel d => new DoctorPricesWindow(d),
            CaseEditorViewModel c => new CaseEditorWindow(c),
            InvoiceEditorViewModel i => new InvoiceEditorWindow(i),
            PaymentEditorViewModel p => new PaymentEditorWindow(p),
            AdjustmentEditorViewModel a => new AdjustmentEditorWindow(a),
            PatientHistoryViewModel h => new PatientHistoryWindow(h),
            _ => throw new NotSupportedException(viewModel.GetType().Name)
        };
        window.Owner = Owner;
        return window.ShowDialog();
    }

    public void ShowPrintPreview(PrintKind kind, int id)
    {
        var docs = services.GetRequiredService<IDocumentService>();
        Func<DocumentRequest, Task<PrintableDocument>> factory = kind switch
        {
            PrintKind.Invoice => r => docs.CreateInvoiceAsync(id, r),
            PrintKind.Receipt => r => docs.CreateReceiptAsync(id, r),
            PrintKind.Statement => r => docs.CreateStatementAsync(id, r),
            _ => r => docs.CreateWorkOrderAsync(id, r)
        };
        var titleKey = kind switch
        {
            PrintKind.Invoice => "Doc_Invoice", PrintKind.Receipt => "Doc_ReceiptTitle",
            PrintKind.Statement => "Doc_Statement", _ => "Doc_WorkOrder"
        };

        var vm = new PrintPreviewViewModel(factory, services.GetRequiredService<ILabSettingsService>(),
            services.GetRequiredService<FileDialogService>(),
            services.GetRequiredService<ILoggerFactory>().CreateLogger<PrintPreviewViewModel>(), titleKey);
        new PrintPreviewWindow(vm) { Owner = Owner }.ShowDialog();
    }
}
