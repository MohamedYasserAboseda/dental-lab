using DentalLab.Domain.Entities;
using DentalLab.Domain.Services;
using Xunit;

namespace DentalLab.Domain.Tests;

public class BillingTests
{
    private static readonly DateTime Now = new(2026, 6, 1, 9, 0, 0, DateTimeKind.Utc);

    private static LabCase Case(int doctor = 1, decimal price = 100m, int qty = 1, decimal discount = 0m)
        => LabCase.Create("C-2026-000001", doctor, 1, 1, null, null, qty, price, discount, Now.AddDays(5), null, [11], Now);

    [Fact]
    public void Invoice_TotalsAreSnapshotsOfTheCases()
    {
        var inv = Invoice.Create("INV-2026-000001", 1, [Case(price: 100m, qty: 2), Case(price: 50m, discount: 10m)], 15m, "note", Now);
        Assert.Equal(240m, inv.Subtotal);   // 200 + 40
        Assert.Equal(225m, inv.Total);
        Assert.Equal(2, inv.Lines.Count);
        Assert.Equal(InvoiceStatus.Unpaid, inv.Status);
        Assert.Equal(225m, inv.Remaining);
    }

    [Fact]
    public void Invoice_RejectsMixedDoctors_AlreadyInvoiced_Cancelled_AndEmpty()
    {
        Assert.Throws<ArgumentException>(() => Invoice.Create("X", 1, [], 0, null, Now));
        Assert.Throws<InvalidOperationException>(() => Invoice.Create("X", 1, [Case(doctor: 2)], 0, null, Now));

        var billed = Case(); billed.AssignInvoice(5);
        Assert.Throws<InvalidOperationException>(() => Invoice.Create("X", 1, [billed], 0, null, Now));

        var cancelled = Case(); cancelled.ChangeStatus(CaseStatus.Cancelled, null, Now);
        Assert.Throws<InvalidOperationException>(() => Invoice.Create("X", 1, [cancelled], 0, null, Now));
    }

    [Theory]
    [InlineData(-1)]
    [InlineData(100.01)]
    [InlineData(100)]   // total would be zero
    public void Invoice_InvalidDiscount_Throws(double discount)
        => Assert.Throws<ArgumentOutOfRangeException>(() => Invoice.Create("X", 1, [Case()], (decimal)discount, null, Now));

    [Fact]
    public void Payments_MoveStatusUnpaidPartialPaid_AndCannotOverpay()
    {
        var inv = Invoice.Create("X", 1, [Case(price: 100m)], 0, null, Now);
        inv.ApplyPayment(40m);
        Assert.Equal(InvoiceStatus.PartiallyPaid, inv.Status);
        Assert.Equal(60m, inv.Remaining);
        Assert.Throws<InvalidOperationException>(() => inv.ApplyPayment(60.01m));
        inv.ApplyPayment(60m);
        Assert.Equal(InvoiceStatus.Paid, inv.Status);
        Assert.Equal(0m, inv.Remaining);
    }

    [Fact]
    public void ReversePayment_RestoresRemaining()
    {
        var inv = Invoice.Create("X", 1, [Case(price: 100m)], 0, null, Now);
        inv.ApplyPayment(100m);
        inv.ReversePayment(100m);
        Assert.Equal(InvoiceStatus.Unpaid, inv.Status);
        Assert.Equal(100m, inv.Remaining);
        Assert.Throws<ArgumentOutOfRangeException>(() => inv.ReversePayment(1m));
    }

    [Fact]
    public void Void_OnlyWithoutPayments_AndOnlyOnce()
    {
        var paid = Invoice.Create("X", 1, [Case()], 0, null, Now);
        paid.ApplyPayment(10m);
        Assert.Throws<InvalidOperationException>(() => paid.Void(Now, null));

        var inv = Invoice.Create("Y", 1, [Case()], 0, null, Now);
        inv.Void(Now, " wrong doctor ");
        Assert.Equal(InvoiceStatus.Voided, inv.Status);
        Assert.Equal("wrong doctor", inv.VoidReason);
        Assert.Equal(0m, inv.Remaining);
        Assert.Throws<InvalidOperationException>(() => inv.Void(Now, null));
        Assert.Throws<InvalidOperationException>(() => inv.ApplyPayment(1m));
    }

    [Fact]
    public void Payment_Create_ValidatesAmount_AndReverseOnlyOnce()
    {
        Assert.Throws<ArgumentOutOfRangeException>(() => Payment.Create("RC-1", 1, null, Now, 0m, PaymentMethod.Cash, null, Now));
        var p = Payment.Create("RC-1", 1, null, Now.AddHours(5), 10.005m, PaymentMethod.Card, " note ", Now);
        Assert.Equal(10.01m, p.Amount);
        Assert.Equal(Now.Date, p.Date);
        Assert.Equal("note", p.Notes);
        p.Reverse(Now);
        Assert.True(p.IsReversed);
        Assert.Throws<InvalidOperationException>(() => p.Reverse(Now));
    }

    [Fact]
    public void BilledCase_CannotBeEditedCancelledOrInvoicedAgain_UntilReleased()
    {
        var c = Case();
        c.AssignInvoice(1);
        Assert.Throws<InvalidOperationException>(() => c.AssignInvoice(2));
        Assert.Throws<InvalidOperationException>(() => c.UpdateDetails(1, 1, 1, null, null, 1, 1m, 0m, Now, null, [11]));
        Assert.Throws<InvalidOperationException>(() => c.ChangeStatus(CaseStatus.Cancelled, null, Now));
        c.ChangeStatus(CaseStatus.Designing, null, Now); // normal production still works
        c.ReleaseInvoice();
        c.AssignInvoice(3);
        Assert.Equal(3, c.InvoiceId);
    }

    [Fact]
    public void DocumentNumber_FormatsAndParses()
    {
        Assert.Equal("INV-2026-000007", DocumentNumber.Format(DocumentNumber.Invoice, 2026, 7));
        Assert.Equal("RC-2026-", DocumentNumber.Prefix(DocumentNumber.Receipt, 2026));
        Assert.Equal(7, DocumentNumber.ParseSequence("INV-2026-000007"));
    }
}
