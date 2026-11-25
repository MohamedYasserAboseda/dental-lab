using DentalLab.Application.Abstractions;
using DentalLab.Application.Billing;
using DentalLab.Application.Cases;
using DentalLab.Application.Catalog;
using DentalLab.Application.Doctors;
using DentalLab.Application.Patients;
using DentalLab.Infrastructure.Backup;
using DentalLab.Infrastructure.Documents;
using DentalLab.Infrastructure.Persistence;
using DentalLab.Shared;
using FluentValidation;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace DentalLab.Infrastructure;

public static class DependencyInjection
{
    public static IServiceCollection AddInfrastructure(this IServiceCollection services)
    {
        services.AddDbContextFactory<DentalLabDbContext>(o =>
            o.UseSqlite($"Data Source={AppPaths.DatabaseFile};Foreign Keys=True"));
        services.AddSingleton<IDatabaseInitializer, DatabaseInitializer>();
        services.AddSingleton<ISettingsService, SettingsService>();

        services.AddSingleton<IValidator<DoctorInput>, DoctorInputValidator>();
        services.AddSingleton<IValidator<PatientInput>, PatientInputValidator>();
        services.AddSingleton<IValidator<CatalogItemInput>, CatalogItemInputValidator>();
        services.AddSingleton<IValidator<CaseInput>, CaseInputValidator>();
        services.AddSingleton<IValidator<InvoiceInput>, InvoiceInputValidator>();
        services.AddSingleton<IValidator<PaymentInput>, PaymentInputValidator>();
        services.AddSingleton<IValidator<AdjustmentInput>, AdjustmentInputValidator>();

        services.AddSingleton<IDoctorService, DoctorService>();
        services.AddSingleton<IPatientService, PatientService>();
        services.AddSingleton<ICatalogService, CatalogService>();
        services.AddSingleton<ICaseService, CaseService>();
        services.AddSingleton<IBillingService, BillingService>();
        services.AddSingleton<IDashboardService, DashboardService>();
        services.AddSingleton<IWorkflowService, WorkflowService>();
        services.AddSingleton<IAttachmentService>(sp => new AttachmentService(
            sp.GetRequiredService<IDbContextFactory<DentalLabDbContext>>(), AppPaths.CaseFiles, new DentalLab.Application.Attachments.AttachmentLimits()));
        services.AddSingleton<ILabSettingsService>(sp => new LabSettingsService(sp.GetRequiredService<ISettingsService>(), AppPaths.Branding));
        services.AddSingleton(DocumentFolders.Default);
        services.AddSingleton<IDocumentService, PdfDocumentService>();
        services.AddSingleton(BackupLocations.Default);
        services.AddSingleton<IBackupService, BackupService>();
        return services;
    }
}
