using InvoiceApp.Domain;
using Xunit;

namespace InvoiceApp.Tests;

public class ApprovalServiceTests
{
    public static TheoryData<string, decimal> ApprovalLimitCases => new()
    {
        { "Manager", 49_999.99m },
        { "Manager", 50_000m },
        { "Director", 499_999.99m },
        { "Director", 500_000m }
    };

    private static Invoice InvoiceWith(
        string id,
        InvoiceStatus status,
        decimal amount = 100m) =>
        new()
        {
            Id = id,
            CustomerName = "Test School",
            Amount = amount,
            Status = status,
            CreatedAt = DateTime.UtcNow.AddDays(-1),
            DecidedBy = "existing.approver",
            DecidedAt = DateTime.UtcNow.AddHours(-1),
            DecisionNote = "Existing decision note"
        };

    private static (ApprovalService Service, FakeInvoiceRepository Repository) ServiceFor(Invoice invoice)
    {
        var repository = new FakeInvoiceRepository(invoice);
        return (new ApprovalService(repository), repository);
    }

    private static Invoice Snapshot(Invoice invoice) => new()
    {
        Id = invoice.Id,
        CustomerName = invoice.CustomerName,
        Amount = invoice.Amount,
        Status = invoice.Status,
        CreatedAt = invoice.CreatedAt,
        DecidedBy = invoice.DecidedBy,
        DecidedAt = invoice.DecidedAt,
        DecisionNote = invoice.DecisionNote
    };

    private static void AssertUnchanged(Invoice invoice, Invoice before, FakeInvoiceRepository repository)
    {
        Assert.Equal(before.Id, invoice.Id);
        Assert.Equal(before.CustomerName, invoice.CustomerName);
        Assert.Equal(before.Amount, invoice.Amount);
        Assert.Equal(before.Status, invoice.Status);
        Assert.Equal(before.CreatedAt, invoice.CreatedAt);
        Assert.Equal(before.DecidedBy, invoice.DecidedBy);
        Assert.Equal(before.DecidedAt, invoice.DecidedAt);
        Assert.Equal(before.DecisionNote, invoice.DecisionNote);
        Assert.Equal(0, repository.UpdateCount);
    }

    [Fact]
    public void Submit_MovesDraftToPendingApproval()
    {
        var invoice = InvoiceWith("INV-1", InvoiceStatus.Draft);
        var (service, _) = ServiceFor(invoice);

        var result = service.Submit("INV-1");

        Assert.Equal(InvoiceStatus.PendingApproval, result.Status);
    }

    [Fact]
    public void Submit_RejectsInvoiceThatIsNotADraft()
    {
        var (service, _) = ServiceFor(InvoiceWith("INV-1", InvoiceStatus.Approved));

        Assert.Throws<InvalidOperationException>(() => service.Submit("INV-1"));
    }

    [Theory]
    [MemberData(nameof(ApprovalLimitCases))]
    public void Approve_AcceptsEligibleRoleAtAndBelowLimit(string role, decimal amount)
    {
        var invoice = InvoiceWith("INV-1", InvoiceStatus.PendingApproval, amount);
        var (service, repository) = ServiceFor(invoice);

        var result = service.Approve("INV-1", "demo.approver", role);

        Assert.Equal(InvoiceStatus.Approved, result.Status);
        Assert.Equal("demo.approver", result.DecidedBy);
        Assert.NotNull(result.DecidedAt);
        Assert.Equal(1, repository.UpdateCount);
    }

    [Theory]
    [InlineData("Manager", 50_000.01)]
    [InlineData("Manager", 500_000)]
    [InlineData("Director", 500_000.01)]
    public void Approve_RejectsAmountAboveRoleLimitWithoutMutation(string role, double amount)
    {
        var invoice = InvoiceWith("INV-1", InvoiceStatus.PendingApproval, (decimal)amount);
        var before = Snapshot(invoice);
        var (service, repository) = ServiceFor(invoice);

        var exception = Assert.Throws<InvalidOperationException>(
            () => service.Approve("INV-1", "demo.approver", role));

        Assert.Contains(role, exception.Message);
        Assert.Contains("may approve invoices up to", exception.Message);
        AssertUnchanged(invoice, before, repository);
    }

    [Fact]
    public void Approve_ManagerCannotApproveInvoiceOf499999_99WithoutMutation()
    {
        var invoice = InvoiceWith("INV-1", InvoiceStatus.PendingApproval, 499_999.99m);
        var before = Snapshot(invoice);
        var (service, repository) = ServiceFor(invoice);

        var exception = Assert.Throws<InvalidOperationException>(
            () => service.Approve("INV-1", "demo.approver", "Manager"));

        Assert.Contains("Manager may approve invoices up to 50,000", exception.Message);
        AssertUnchanged(invoice, before, repository);
    }

    [Fact]
    public void Approve_ThrowsWhenInvoiceDoesNotExist()
    {
        var repository = new FakeInvoiceRepository();
        var service = new ApprovalService(repository);

        Assert.Throws<KeyNotFoundException>(() => service.Approve("INV-404", "demo.approver", "Manager"));
        Assert.Equal(0, repository.UpdateCount);
    }

    [Theory]
    [InlineData(InvoiceStatus.Draft)]
    [InlineData(InvoiceStatus.Approved)]
    [InlineData(InvoiceStatus.Rejected)]
    public void Approve_RejectsInvoiceThatIsNotPendingWithoutMutation(InvoiceStatus status)
    {
        var invoice = InvoiceWith("INV-1", status);
        var before = Snapshot(invoice);
        var (service, repository) = ServiceFor(invoice);

        var exception = Assert.Throws<InvalidOperationException>(
            () => service.Approve("INV-1", "demo.approver", "Manager"));

        Assert.Contains("only pending invoices can be decided", exception.Message);
        AssertUnchanged(invoice, before, repository);
    }

    [Fact]
    public void Approve_RejectsUnsupportedRoleWithoutMutation()
    {
        var invoice = InvoiceWith("INV-1", InvoiceStatus.PendingApproval);
        var before = Snapshot(invoice);
        var (service, repository) = ServiceFor(invoice);

        var exception = Assert.Throws<InvalidOperationException>(
            () => service.Approve("INV-1", "demo.approver", "Admin"));

        Assert.Contains("not authorized", exception.Message);
        AssertUnchanged(invoice, before, repository);
    }

    [Fact]
    public void Reject_RecordsReasonAndApprover()
    {
        var (service, _) = ServiceFor(InvoiceWith("INV-1", InvoiceStatus.PendingApproval));

        var result = service.Reject("INV-1", "demo.approver", "Duplicate invoice");

        Assert.Equal(InvoiceStatus.Rejected, result.Status);
        Assert.Equal("Duplicate invoice", result.DecisionNote);
    }

    [Fact]
    public void Reject_RequiresAReason()
    {
        var (service, _) = ServiceFor(InvoiceWith("INV-1", InvoiceStatus.PendingApproval));

        Assert.Throws<ArgumentException>(() => service.Reject("INV-1", "demo.approver", "  "));
    }
}
