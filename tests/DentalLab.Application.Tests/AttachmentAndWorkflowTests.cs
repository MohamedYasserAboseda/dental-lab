using DentalLab.Application.Attachments;
using DentalLab.Application.Cases;
using DentalLab.Application.Catalog;
using DentalLab.Application.Doctors;
using DentalLab.Application.Patients;
using DentalLab.Application.Workflow;
using DentalLab.Domain.Entities;
using DentalLab.Infrastructure.Persistence;
using FluentValidation;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Xunit;

namespace DentalLab.Application.Tests;

public class AttachmentRulesTests
{
    private static readonly AttachmentLimits Limits = new(1000, 5000);
    private static readonly byte[] Png = [0x89, 0x50, 0x4E, 0x47, 0x0D, 0x0A, 0x1A, 0x0A, 0, 0];
    private static readonly byte[] Jpg = [0xFF, 0xD8, 0xFF, 0xE0, 0, 0];

    private static byte[] BinaryStl(uint triangles)
    {
        var b = new byte[84];
        BitConverter.GetBytes(triangles).CopyTo(b, 80);
        return b;
    }

    [Fact]
    public void Photos_AreAcceptedByContent()
    {
        Assert.Equal(AttachmentKind.Photo, AttachmentRules.Validate("a.png", Png, 500, Limits).Kind);
        Assert.Equal(AttachmentKind.Photo, AttachmentRules.Validate("a.JPG", Jpg, 500, Limits).Kind);
        Assert.Equal(AttachmentKind.Photo, AttachmentRules.Validate("a.jpeg", Jpg, 500, Limits).Kind);
    }

    [Fact]
    public void Stl_AsciiAndBinary_AreRecognised_ButRandomBytesAreNot()
    {
        Assert.Equal(AttachmentKind.Stl, AttachmentRules.Validate("crown.stl", "solid crown\nfacet normal"u8.ToArray(), 2000, Limits).Kind);
        Assert.Equal(AttachmentKind.Stl, AttachmentRules.Validate("crown.stl", BinaryStl(2), 84 + 100, Limits).Kind);      // size matches the triangle count
        Assert.Equal("ContentMismatch", AttachmentRules.Validate("crown.stl", BinaryStl(2), 84 + 99, Limits).Error);        // truncated file
        Assert.Equal("ContentMismatch", AttachmentRules.Validate("x.stl", new byte[200], 2000, Limits).Error);
    }

    [Fact]
    public void Ply_NeedsItsHeader()
    {
        Assert.Equal(AttachmentKind.Ply, AttachmentRules.Validate("s.ply", "ply\nformat ascii 1.0"u8.ToArray(), 100, Limits).Kind);
        Assert.Equal(AttachmentKind.Ply, AttachmentRules.Validate("s.ply", "ply\r\nformat"u8.ToArray(), 100, Limits).Kind);
        Assert.Equal("ContentMismatch", AttachmentRules.Validate("s.ply", "plx\n"u8.ToArray(), 100, Limits).Error);
    }

    [Theory]
    [InlineData("virus.exe")] [InlineData("notes.txt")] [InlineData("scan.zip")] [InlineData("noextension")] [InlineData("a.png.exe")]
    public void OtherTypes_AreRejected(string name)
        => Assert.Equal("UnsupportedType", AttachmentRules.Validate(name, Png, 100, Limits).Error);

    [Fact]
    public void RenamedFiles_AreRejected_BecauseContentMustMatch()
    {
        Assert.Equal("ContentMismatch", AttachmentRules.Validate("photo.png", "MZ\x90\x00 this is an exe"u8.ToArray(), 100, Limits).Error);
        Assert.Equal("ContentMismatch", AttachmentRules.Validate("scan.stl", Png, 100, Limits).Error);
    }

    [Fact]
    public void SizeLimits_AreEnforcedPerKind_AndEmptyFilesAreRejected()
    {
        Assert.Equal("FileTooLarge", AttachmentRules.Validate("a.png", Png, 1001, Limits).Error);
        Assert.Equal("FileTooLarge", AttachmentRules.Validate("a.ply", "ply\n"u8.ToArray(), 5001, Limits).Error);
        Assert.Null(AttachmentRules.Validate("a.ply", "ply\n"u8.ToArray(), 4000, Limits).Error);   // models are allowed to be bigger than photos
        Assert.Equal("FileEmpty", AttachmentRules.Validate("a.png", Png, 0, Limits).Error);
    }
}

public sealed class AttachmentAndWorkflowServiceTests : IDisposable
{
    private sealed class Factory(DbContextOptions<DentalLabDbContext> o) : IDbContextFactory<DentalLabDbContext>
    {
        public DentalLabDbContext CreateDbContext() => new(o);
    }

    private readonly SqliteConnection _conn = new("Data Source=:memory:");
    private readonly string _dir = Path.Combine(Path.GetTempPath(), "dentallab-att-" + Guid.NewGuid().ToString("N"));
    private readonly Factory _factory;
    private readonly DoctorService _doctors;
    private readonly PatientService _patients;
    private readonly CatalogService _catalog;
    private readonly CaseService _cases;
    private readonly AttachmentService _attachments;
    private readonly WorkflowService _workflow;

    public AttachmentAndWorkflowServiceTests()
    {
        Directory.CreateDirectory(_dir);
        _conn.Open();
        _factory = new Factory(new DbContextOptionsBuilder<DentalLabDbContext>().UseSqlite(_conn).Options);
        using (var db = _factory.CreateDbContext()) { db.Database.EnsureCreated(); CatalogSeeder.SeedAsync(db).GetAwaiter().GetResult(); }
        _doctors = new DoctorService(_factory, new DoctorInputValidator());
        _patients = new PatientService(_factory, new PatientInputValidator());
        _catalog = new CatalogService(_factory, new CatalogItemInputValidator());
        _cases = new CaseService(_factory, new CaseInputValidator());
        _attachments = new AttachmentService(_factory, Path.Combine(_dir, "Cases"), new AttachmentLimits(10_000, 20_000));
        _workflow = new WorkflowService(_factory);
    }

    public void Dispose() { _conn.Dispose(); try { Directory.Delete(_dir, true); } catch (IOException) { } }

    private async Task<(int d, int p, int t)> DoctorAsync(string name = "Dr A")
    {
        var d = await _doctors.CreateAsync(new DoctorInput(name, null, null, null, null, null, 0, true));
        var p = await _patients.CreateAsync(new PatientInput("Pat " + name, d, null, null));
        return (d, p, (await _catalog.GetRestorationTypesAsync(true)).First().Id);
    }

    private Task<int> CaseAsync(int d, int p, int t, int daysToDeliver = 5, int[]? teeth = null)
        => _cases.CreateAsync(new CaseInput(d, p, t, null, null, 1, 100m, 0m, DateTime.Today.AddDays(daysToDeliver), null, teeth ?? [11]));

    private string Write(string name, byte[] bytes) { var p = Path.Combine(_dir, name); File.WriteAllBytes(p, bytes); return p; }
    private static readonly byte[] Png = [0x89, 0x50, 0x4E, 0x47, 0x0D, 0x0A, 0x1A, 0x0A, 0, 0, 0, 0];

    // ------------------------------------------------------------ attachments

    [Fact]
    public async Task Add_CopiesTheFileUnderTheCaseFolder_WithAGeneratedName_AndRecordsIt()
    {
        var (d, p, t) = await DoctorAsync();
        var c = await CaseAsync(d, p, t);
        var source = Write("patient scan (final).stl", "solid scan\nendsolid"u8.ToArray());

        var item = await _attachments.AddAsync(c, source);
        Assert.Equal("patient scan (final).stl", item.FileName);
        Assert.Equal(AttachmentKind.Stl, item.Kind);

        var stored = await _attachments.GetFilePathAsync(item.Id);
        Assert.NotNull(stored);
        Assert.StartsWith(Path.Combine(_dir, "Cases", c.ToString()), stored);
        Assert.DoesNotContain("patient scan", Path.GetFileName(stored!));       // the user's name never becomes a path
        Assert.Equal(File.ReadAllBytes(source), File.ReadAllBytes(stored!));
        Assert.Contains(await _attachments.ListAsync(c), a => a.Id == item.Id);

        await using var db = _factory.CreateDbContext();
        Assert.Contains(db.AuditLogs, a => a.Action == "AttachmentAdded");
        Assert.Equal(64, (await db.CaseAttachments.SingleAsync()).Sha256.Length);
    }

    [Fact]
    public async Task Add_RejectsBadFiles_AndLeavesNothingOnDisk()
    {
        var (d, p, t) = await DoctorAsync();
        var c = await CaseAsync(d, p, t);

        async Task<string> Code(string path)
            => (await Assert.ThrowsAsync<ValidationException>(() => _attachments.AddAsync(c, path))).Errors.First().ErrorCode;

        Assert.Equal("UnsupportedType", await Code(Write("run.exe", "MZ"u8.ToArray())));
        Assert.Equal("ContentMismatch", await Code(Write("fake.png", "not an image at all"u8.ToArray())));
        Assert.Equal("FileEmpty", await Code(Write("empty.png", [])));
        Assert.Equal("FileTooLarge", await Code(Write("huge.png", Png.Concat(new byte[10_000]).ToArray())));
        Assert.Equal("FileMissing", await Code(Path.Combine(_dir, "nothing.png")));

        Assert.Empty(await _attachments.ListAsync(c));
        var caseFolder = Path.Combine(_dir, "Cases", c.ToString());
        Assert.True(!Directory.Exists(caseFolder) || Directory.GetFiles(caseFolder).Length == 0);
    }

    [Fact]
    public async Task Add_ToAMissingCase_IsRejected()
    {
        var ex = await Assert.ThrowsAsync<ValidationException>(() => _attachments.AddAsync(999, Write("a.png", Png)));
        Assert.Contains(ex.Errors, e => e.ErrorCode == "CaseNotFound");
    }

    [Fact]
    public async Task Remove_DeletesTheRecordAndTheFile_AndIsAudited()
    {
        var (d, p, t) = await DoctorAsync();
        var c = await CaseAsync(d, p, t);
        var item = await _attachments.AddAsync(c, Write("a.png", Png));
        var stored = (await _attachments.GetFilePathAsync(item.Id))!;

        await _attachments.RemoveAsync(item.Id);
        Assert.False(File.Exists(stored));
        Assert.Empty(await _attachments.ListAsync(c));
        Assert.Null(await _attachments.GetFilePathAsync(item.Id));
        await using var db = _factory.CreateDbContext();
        Assert.Contains(db.AuditLogs, a => a.Action == "AttachmentRemoved");
    }

    [Fact]
    public async Task Attachments_StayInsideTheirOwnCase_AndSameNamesDoNotCollide()
    {
        var (d, p, t) = await DoctorAsync();
        var c1 = await CaseAsync(d, p, t);
        var c2 = await CaseAsync(d, p, t);
        var a1 = await _attachments.AddAsync(c1, Write("same.png", Png));
        var a2 = await _attachments.AddAsync(c1, Write("same.png", Png));
        var a3 = await _attachments.AddAsync(c2, Write("same.png", Png));

        Assert.Equal(2, (await _attachments.ListAsync(c1)).Count);
        Assert.Single(await _attachments.ListAsync(c2));
        Assert.NotEqual(await _attachments.GetFilePathAsync(a1.Id), await _attachments.GetFilePathAsync(a2.Id));
        Assert.Contains(Path.Combine("Cases", c2.ToString()), (await _attachments.GetFilePathAsync(a3.Id))!);
    }

    [Fact]
    public async Task GetFilePath_ReturnsNull_WhenTheFileWasDeletedOutsideTheApp()
    {
        var (d, p, t) = await DoctorAsync();
        var c = await CaseAsync(d, p, t);
        var item = await _attachments.AddAsync(c, Write("a.png", Png));
        File.Delete((await _attachments.GetFilePathAsync(item.Id))!);
        Assert.Null(await _attachments.GetFilePathAsync(item.Id));
        Assert.Single(await _attachments.ListAsync(c));              // the record is still listed so the user can remove it
    }

    // ------------------------------------------------------------ workflow board

    [Fact]
    public async Task Board_PlacesOpenCasesInTheirStage_AndLeavesOutDeliveredAndCancelled()
    {
        var (d, p, t) = await DoctorAsync();
        var received = await CaseAsync(d, p, t);
        var designing = await CaseAsync(d, p, t);
        var ready = await CaseAsync(d, p, t);
        var delivered = await CaseAsync(d, p, t);
        var cancelled = await CaseAsync(d, p, t);

        await _cases.ChangeStatusAsync(designing, CaseStatus.Designing, null);
        foreach (var s in new[] { CaseStatus.Production, CaseStatus.Finishing, CaseStatus.QualityControl, CaseStatus.Ready })
        {
            await _cases.ChangeStatusAsync(ready, s, null);
            await _cases.ChangeStatusAsync(delivered, s, null);
        }
        await _cases.ChangeStatusAsync(delivered, CaseStatus.Delivered, null);
        await _cases.ChangeStatusAsync(cancelled, CaseStatus.Cancelled, null);

        var board = await _workflow.GetBoardAsync(new BoardQuery(null, false));
        Assert.Equal(6, board.Columns.Count);
        Assert.Equal(received, Assert.Single(board.Columns.Single(c => c.Status == CaseStatus.Received).Cards).CaseId);
        Assert.Equal(designing, Assert.Single(board.Columns.Single(c => c.Status == CaseStatus.Designing).Cards).CaseId);
        Assert.Equal(ready, Assert.Single(board.Columns.Single(c => c.Status == CaseStatus.Ready).Cards).CaseId);
        Assert.Equal(3, board.Columns.Sum(c => c.TotalCount));
        Assert.Equal(3, board.Stats.InProgress);
    }

    [Fact]
    public async Task Board_SortsByDueDate_FlagsOverdue_AndCountsDueToday()
    {
        var (d, p, t) = await DoctorAsync();
        var later = await CaseAsync(d, p, t, daysToDeliver: 9);
        var today = await CaseAsync(d, p, t, daysToDeliver: 0);
        var late = await CaseAsync(d, p, t, daysToDeliver: -3);

        var board = await _workflow.GetBoardAsync(new BoardQuery(null, false));
        var cards = board.Columns.Single(c => c.Status == CaseStatus.Received).Cards;
        Assert.Equal(new[] { late, today, later }, cards.Select(c => c.CaseId).ToArray());
        Assert.True(cards[0].IsOverdue);
        Assert.False(cards[1].IsOverdue);
        Assert.Equal(1, board.Stats.Overdue);
        Assert.Equal(1, board.Stats.DueToday);

        var onlyLate = await _workflow.GetBoardAsync(new BoardQuery(null, true));
        Assert.Equal(late, Assert.Single(onlyLate.Columns.SelectMany(c => c.Cards)).CaseId);
    }

    [Fact]
    public async Task Board_FiltersByDoctor_AndCarriesTeethAndNames()
    {
        var (d1, p1, t) = await DoctorAsync("Dr One");
        var (d2, p2, _) = await DoctorAsync("Dr Two");
        var mine = await CaseAsync(d1, p1, t, teeth: [21, 11]);
        await CaseAsync(d2, p2, t);

        var board = await _workflow.GetBoardAsync(new BoardQuery(d1, false));
        var card = Assert.Single(board.Columns.SelectMany(c => c.Cards));
        Assert.Equal(mine, card.CaseId);
        Assert.Equal(new[] { 11, 21 }, card.Teeth);
        Assert.Equal("Dr One", card.DoctorName);
        Assert.Equal("Pat Dr One", card.PatientName);
        Assert.Equal(1, board.Stats.InProgress);
    }

    [Fact]
    public async Task Stats_CountRecentDeliveries_AndAverageTurnaround()
    {
        var (d, p, t) = await DoctorAsync();
        Assert.Null((await _workflow.GetBoardAsync(new BoardQuery(null, false))).Stats.AverageTurnaroundDays);

        var c = await CaseAsync(d, p, t);
        foreach (var s in new[] { CaseStatus.Production, CaseStatus.Finishing, CaseStatus.QualityControl, CaseStatus.Ready, CaseStatus.Delivered })
            await _cases.ChangeStatusAsync(c, s, null);

        var stats = (await _workflow.GetBoardAsync(new BoardQuery(null, false))).Stats;
        Assert.Equal(1, stats.DeliveredLast30Days);
        Assert.NotNull(stats.AverageTurnaroundDays);
        Assert.InRange(stats.AverageTurnaroundDays!.Value, 0, 1);      // it was delivered a moment after it was received
        Assert.Equal(0, stats.InProgress);
    }

    // ------------------------------------------------------------ patient history filter

    [Fact]
    public async Task CaseSearch_CanBeLimitedToOnePatient()
    {
        var (d, p1, t) = await DoctorAsync();
        var p2 = await _patients.CreateAsync(new PatientInput("Second patient", d, null, null));
        var c1 = await CaseAsync(d, p1, t);
        await _cases.CreateAsync(new CaseInput(d, p2, t, null, null, 1, 10m, 0m, DateTime.Today.AddDays(3), null, [12]));

        var result = await _cases.SearchAsync(new CaseQuery(null, null, null, null, null, false, 1, 20) { PatientId = p1 });
        Assert.Equal(c1, Assert.Single(result.Items).Id);
    }
}
