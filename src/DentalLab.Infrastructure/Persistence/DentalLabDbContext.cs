using DentalLab.Domain.Entities;
using Microsoft.EntityFrameworkCore;

namespace DentalLab.Infrastructure.Persistence;

public class DentalLabDbContext(DbContextOptions<DentalLabDbContext> options) : DbContext(options)
{
    public DbSet<AppSetting> Settings => Set<AppSetting>();
    public DbSet<AuditLog> AuditLogs => Set<AuditLog>();
    public DbSet<Doctor> Doctors => Set<Doctor>();
    public DbSet<LedgerEntry> LedgerEntries => Set<LedgerEntry>();
    public DbSet<Patient> Patients => Set<Patient>();
    public DbSet<RestorationType> RestorationTypes => Set<RestorationType>();
    public DbSet<Material> Materials => Set<Material>();
    public DbSet<DoctorPrice> DoctorPrices => Set<DoctorPrice>();
    public DbSet<LabCase> Cases => Set<LabCase>();
    public DbSet<CaseTooth> CaseTeeth => Set<CaseTooth>();
    public DbSet<CaseStatusHistory> CaseStatusHistory => Set<CaseStatusHistory>();
    public DbSet<CaseAttachment> CaseAttachments => Set<CaseAttachment>();
    public DbSet<Invoice> Invoices => Set<Invoice>();
    public DbSet<InvoiceLine> InvoiceLines => Set<InvoiceLine>();
    public DbSet<Payment> Payments => Set<Payment>();

    protected override void OnModelCreating(ModelBuilder b)
    {
        b.Entity<AppSetting>(e =>
        {
            e.ToTable("Settings");
            e.HasKey(x => x.Key);
            e.Property(x => x.Key).HasMaxLength(100);
            e.Property(x => x.Value).IsRequired();
        });

        b.Entity<AuditLog>(e =>
        {
            e.ToTable("AuditLogs");
            e.HasKey(x => x.Id);
            e.Property(x => x.Action).HasMaxLength(100).IsRequired();
            e.Property(x => x.EntityName).HasMaxLength(100);
            e.Property(x => x.EntityId).HasMaxLength(50);
            e.HasIndex(x => x.TimestampUtc);
        });

        b.Entity<Doctor>(e =>
        {
            e.ToTable("Doctors");
            e.HasKey(x => x.Id);
            e.Ignore(x => x.Code);
            e.Property(x => x.FullName).HasMaxLength(150).IsRequired();
            e.Property(x => x.ClinicName).HasMaxLength(150);
            e.Property(x => x.Phone).HasMaxLength(30);
            e.Property(x => x.Email).HasMaxLength(150);
            e.Property(x => x.Address).HasMaxLength(300);
            e.Property(x => x.Notes).HasMaxLength(2000);
            e.Property(x => x.OpeningBalance).HasPrecision(18, 2);
            e.HasIndex(x => x.FullName);
            e.HasIndex(x => x.Phone);
            e.HasMany(x => x.LedgerEntries).WithOne(x => x.Doctor!)
                .HasForeignKey(x => x.DoctorId).OnDelete(DeleteBehavior.Restrict);
        });

        b.Entity<LedgerEntry>(e =>
        {
            e.ToTable("LedgerEntries");
            e.HasKey(x => x.Id);
            e.Property(x => x.Amount).HasPrecision(18, 2);
            e.Property(x => x.Description).HasMaxLength(500);
            e.HasIndex(x => new { x.DoctorId, x.DateUtc });
            e.HasIndex(x => x.InvoiceId);
            e.HasIndex(x => x.PaymentId);
        });

        b.Entity<Patient>(e =>
        {
            e.ToTable("Patients");
            e.HasKey(x => x.Id);
            e.Property(x => x.FullName).HasMaxLength(150).IsRequired();
            e.Property(x => x.Code).HasMaxLength(30);
            e.Property(x => x.Notes).HasMaxLength(1000);
            e.HasIndex(x => x.FullName);
            e.HasIndex(x => x.DoctorId);
            e.HasOne(x => x.Doctor).WithMany().HasForeignKey(x => x.DoctorId).OnDelete(DeleteBehavior.Restrict);
        });

        b.Entity<RestorationType>(e =>
        {
            e.ToTable("RestorationTypes");
            e.HasKey(x => x.Id);
            e.Property(x => x.Name).HasMaxLength(100).IsRequired();
            e.Property(x => x.NameAr).HasMaxLength(100);
            e.Property(x => x.DefaultUnitPrice).HasPrecision(18, 2);
            e.HasIndex(x => x.Name).IsUnique();
        });

        b.Entity<Material>(e =>
        {
            e.ToTable("Materials");
            e.HasKey(x => x.Id);
            e.Property(x => x.Name).HasMaxLength(100).IsRequired();
            e.Property(x => x.NameAr).HasMaxLength(100);
            e.HasIndex(x => x.Name).IsUnique();
        });

        b.Entity<DoctorPrice>(e =>
        {
            e.ToTable("DoctorPrices");
            e.HasKey(x => x.Id);
            e.Property(x => x.UnitPrice).HasPrecision(18, 2);
            e.HasIndex(x => new { x.DoctorId, x.RestorationTypeId }).IsUnique();
            e.HasOne<Doctor>().WithMany().HasForeignKey(x => x.DoctorId).OnDelete(DeleteBehavior.Restrict);
            e.HasOne<RestorationType>().WithMany().HasForeignKey(x => x.RestorationTypeId).OnDelete(DeleteBehavior.Restrict);
        });

        b.Entity<LabCase>(e =>
        {
            e.ToTable("Cases");
            e.HasKey(x => x.Id);
            e.Property(x => x.CaseNumber).HasMaxLength(30).IsRequired();
            e.Property(x => x.Shade).HasMaxLength(30);
            e.Property(x => x.Notes).HasMaxLength(2000);
            e.Property(x => x.UnitPrice).HasPrecision(18, 2);
            e.Property(x => x.Discount).HasPrecision(18, 2);
            e.Property(x => x.Total).HasPrecision(18, 2);
            e.HasIndex(x => x.CaseNumber).IsUnique();
            e.HasIndex(x => x.DoctorId);
            e.HasIndex(x => x.PatientId);
            e.HasIndex(x => x.Status);
            e.HasIndex(x => x.ExpectedDeliveryDate);
            e.HasIndex(x => x.ReceivedUtc);
            e.Ignore(x => x.IsEditable);
            e.Ignore(x => x.IsInvoiced);
            e.HasIndex(x => x.InvoiceId);
            e.HasOne(x => x.Doctor).WithMany().HasForeignKey(x => x.DoctorId).OnDelete(DeleteBehavior.Restrict);
            e.HasOne(x => x.Patient).WithMany().HasForeignKey(x => x.PatientId).OnDelete(DeleteBehavior.Restrict);
            e.HasOne(x => x.RestorationType).WithMany().HasForeignKey(x => x.RestorationTypeId).OnDelete(DeleteBehavior.Restrict);
            e.HasOne(x => x.Material).WithMany().HasForeignKey(x => x.MaterialId).OnDelete(DeleteBehavior.Restrict);
            e.HasMany(x => x.Teeth).WithOne().HasForeignKey(x => x.CaseId).OnDelete(DeleteBehavior.Cascade);
            e.HasMany(x => x.History).WithOne().HasForeignKey(x => x.CaseId).OnDelete(DeleteBehavior.Cascade);
        });

        b.Entity<CaseAttachment>(e =>
        {
            e.ToTable("CaseAttachments");
            e.HasKey(x => x.Id);
            e.Property(x => x.FileName).HasMaxLength(260).IsRequired();
            e.Property(x => x.StoredFileName).HasMaxLength(100).IsRequired();
            e.Property(x => x.Sha256).HasMaxLength(64);
            e.HasIndex(x => x.CaseId);
            e.HasOne<LabCase>().WithMany().HasForeignKey(x => x.CaseId).OnDelete(DeleteBehavior.Restrict);
        });

        b.Entity<Invoice>(e =>
        {
            e.ToTable("Invoices");
            e.HasKey(x => x.Id);
            e.Ignore(x => x.Remaining);
            e.Property(x => x.InvoiceNumber).HasMaxLength(30).IsRequired();
            e.Property(x => x.Description).HasMaxLength(500);
            e.Property(x => x.VoidReason).HasMaxLength(500);
            e.Property(x => x.Subtotal).HasPrecision(18, 2);
            e.Property(x => x.Discount).HasPrecision(18, 2);
            e.Property(x => x.Total).HasPrecision(18, 2);
            e.Property(x => x.PaidAmount).HasPrecision(18, 2);
            e.HasIndex(x => x.InvoiceNumber).IsUnique();
            e.HasIndex(x => x.DoctorId);
            e.HasIndex(x => x.Status);
            e.HasIndex(x => x.DateUtc);
            e.HasOne(x => x.Doctor).WithMany().HasForeignKey(x => x.DoctorId).OnDelete(DeleteBehavior.Restrict);
            e.HasMany(x => x.Lines).WithOne().HasForeignKey(x => x.InvoiceId).OnDelete(DeleteBehavior.Cascade);
        });

        b.Entity<InvoiceLine>(e =>
        {
            e.ToTable("InvoiceLines");
            e.HasKey(x => x.Id);
            e.Property(x => x.Description).HasMaxLength(200);
            e.Property(x => x.UnitPrice).HasPrecision(18, 2);
            e.Property(x => x.Discount).HasPrecision(18, 2);
            e.Property(x => x.Amount).HasPrecision(18, 2);
            e.HasIndex(x => x.CaseId);
        });

        b.Entity<Payment>(e =>
        {
            e.ToTable("Payments");
            e.HasKey(x => x.Id);
            e.Property(x => x.ReceiptNumber).HasMaxLength(30).IsRequired();
            e.Property(x => x.Notes).HasMaxLength(500);
            e.Property(x => x.Amount).HasPrecision(18, 2);
            e.HasIndex(x => x.ReceiptNumber).IsUnique();
            e.HasIndex(x => x.DoctorId);
            e.HasIndex(x => x.InvoiceId);
            e.HasIndex(x => x.Date);
            e.HasOne(x => x.Doctor).WithMany().HasForeignKey(x => x.DoctorId).OnDelete(DeleteBehavior.Restrict);
        });

        b.Entity<CaseTooth>(e =>
        {
            e.ToTable("CaseTeeth");
            e.HasKey(x => x.Id);
            e.HasIndex(x => x.CaseId);
        });

        b.Entity<CaseStatusHistory>(e =>
        {
            e.ToTable("CaseStatusHistory");
            e.HasKey(x => x.Id);
            e.Property(x => x.Note).HasMaxLength(500);
            e.Property(x => x.ChangedBy).HasMaxLength(100);
            e.HasIndex(x => x.CaseId);
        });
    }
}
