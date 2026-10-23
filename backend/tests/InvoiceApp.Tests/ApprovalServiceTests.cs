using InvoiceApp.Domain;
using InvoiceApp.Api;
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
        ApprovalHistory = invoice.ApprovalHistory.ToList(),
        DecidedBy = invoice.DecidedBy,
        DecidedAt = invoice.DecidedAt,
        DecisionNote = invoice.DecisionNote
    };

    private static void AssertUnchanged(
        Invoice invoice,
        Invoice before,
        FakeInvoiceRepository repository,
        int expectedUpdateCount = 0)
    {
        AssertInvoiceMatches(before, invoice);
        var persistedInvoice = repository.GetById(before.Id);
        Assert.NotNull(persistedInvoice);
        AssertInvoiceMatches(before, persistedInvoice);
        Assert.Equal(expectedUpdateCount, repository.UpdateCount);
    }

    private static void AssertInvoiceMatches(Invoice expected, Invoice actual)
    {
        Assert.Equal(expected.Id, actual.Id);
        Assert.Equal(expected.CustomerName, actual.CustomerName);
        Assert.Equal(expected.Amount, actual.Amount);
        Assert.Equal(expected.Status, actual.Status);
        Assert.Equal(expected.CreatedAt, actual.CreatedAt);
        Assert.Equal(expected.DecidedBy, actual.DecidedBy);
        Assert.Equal(expected.DecidedAt, actual.DecidedAt);
        Assert.Equal(expected.DecisionNote, actual.DecisionNote);
        Assert.Equal(expected.ApprovalHistory, actual.ApprovalHistory);
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
        var invoice = InvoiceWith("INV-1", InvoiceStatus.Approved);
        var before = Snapshot(invoice);
        var (service, repository) = ServiceFor(invoice);

        Assert.Throws<InvalidOperationException>(() => service.Submit("INV-1"));
        AssertUnchanged(invoice, before, repository);
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
        Assert.Collection(result.ApprovalHistory,
            approval =>
            {
                Assert.Equal("demo.approver", approval.Approver);
                Assert.Equal(role, approval.Role);
                Assert.Equal(result.DecidedAt, approval.ApprovedAt);
            });
        Assert.Equal(1, repository.UpdateCount);
    }

    [Theory]
    [InlineData("Manager", 50_000.01)]
    [InlineData("Manager", 500_000)]
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

    [Theory]
    [InlineData("Manager")]
    [InlineData("Director")]
    public void Approve_RecordsFirstHighValueApprovalAndLeavesInvoicePending(string role)
    {
        var invoice = InvoiceWith("INV-HIGH", InvoiceStatus.PendingApproval, 500_000.01m);
        invoice.DecidedBy = null;
        invoice.DecidedAt = null;
        invoice.DecisionNote = null;
        var (service, repository) = ServiceFor(invoice);

        var result = service.Approve("INV-HIGH", "first.approver", role);

        Assert.Equal(InvoiceStatus.PendingApproval, result.Status);
        Assert.Null(result.DecidedBy);
        Assert.Null(result.DecidedAt);
        Assert.Null(result.DecisionNote);
        Assert.Collection(result.ApprovalHistory,
            approval =>
            {
                Assert.Equal("first.approver", approval.Approver);
                Assert.Equal(role, approval.Role);
                Assert.NotEqual(default, approval.ApprovedAt);
            });
        Assert.Equal(1, repository.UpdateCount);
    }

    [Fact]
    public void Approve_DistinctDirectorCompletesHighValueInvoice()
    {
        var invoice = InvoiceWith("INV-HIGH", InvoiceStatus.PendingApproval, 500_000.01m);
        invoice.DecidedBy = null;
        invoice.DecidedAt = null;
        invoice.DecisionNote = null;
        var (service, repository) = ServiceFor(invoice);

        service.Approve("INV-HIGH", "first.manager", "Manager");
        var result = service.Approve("INV-HIGH", "second.director", "Director");

        Assert.Equal(InvoiceStatus.Approved, result.Status);
        Assert.Collection(result.ApprovalHistory,
            firstApproval =>
            {
                Assert.Equal("first.manager", firstApproval.Approver);
                Assert.Equal("Manager", firstApproval.Role);
            },
            secondApproval =>
            {
                Assert.Equal("second.director", secondApproval.Approver);
                Assert.Equal("Director", secondApproval.Role);
                Assert.Equal(result.DecidedAt, secondApproval.ApprovedAt);
            });
        Assert.Equal("second.director", result.DecidedBy);
        Assert.NotNull(result.DecidedAt);
        Assert.Equal(2, repository.UpdateCount);
    }

    [Fact]
    public void Approve_RejectsFirstApproverRepeatingWithoutMutation()
    {
        var invoice = InvoiceWith("INV-HIGH", InvoiceStatus.PendingApproval, 500_000.01m);
        invoice.DecidedBy = null;
        invoice.DecidedAt = null;
        invoice.DecisionNote = null;
        var (service, repository) = ServiceFor(invoice);
        service.Approve("INV-HIGH", "first.manager", "Manager");
        var before = Snapshot(repository.GetById("INV-HIGH")!);

        var exception = Assert.Throws<InvalidOperationException>(
            () => service.Approve("INV-HIGH", "FIRST.MANAGER", "Director"));

        Assert.Contains("already provided the first approval", exception.Message);
        AssertUnchanged(repository.GetById("INV-HIGH")!, before, repository, expectedUpdateCount: 1);
    }

    [Fact]
    public void Approve_RejectsNonDirectorSecondApproverWithoutMutation()
    {
        var invoice = InvoiceWith("INV-HIGH", InvoiceStatus.PendingApproval, 500_000.01m);
        invoice.DecidedBy = null;
        invoice.DecidedAt = null;
        invoice.DecisionNote = null;
        var (service, repository) = ServiceFor(invoice);
        service.Approve("INV-HIGH", "first.director", "Director");
        var before = Snapshot(repository.GetById("INV-HIGH")!);

        var exception = Assert.Throws<InvalidOperationException>(
            () => service.Approve("INV-HIGH", "second.manager", "Manager"));

        Assert.Contains("A Director must provide the second approval", exception.Message);
        AssertUnchanged(repository.GetById("INV-HIGH")!, before, repository, expectedUpdateCount: 1);
    }

    [Fact]
    public void Approve_RejectsInvalidHighValueApprovalStageWithoutMutation()
    {
        var invoice = InvoiceWith("INV-HIGH", InvoiceStatus.PendingApproval, 500_000.01m);
        invoice.DecidedBy = null;
        invoice.DecidedAt = null;
        invoice.ApprovalHistory.Add(new InvoiceApproval("first.user", "Manager", DateTime.UtcNow));
        invoice.ApprovalHistory.Add(new InvoiceApproval("second.user", "Director", DateTime.UtcNow));
        var before = Snapshot(invoice);
        var (service, repository) = ServiceFor(invoice);

        var exception = Assert.Throws<InvalidOperationException>(
            () => service.Approve("INV-HIGH", "third.user", "Director"));

        Assert.Contains("invalid approval stage", exception.Message);
        AssertUnchanged(invoice, before, repository);
    }

    [Theory]
    [InlineData(InvoiceStatus.Draft)]
    [InlineData(InvoiceStatus.Approved)]
    [InlineData(InvoiceStatus.Rejected)]
    public void Approve_RejectsHighValueInvoiceThatIsNotPendingWithoutMutation(InvoiceStatus status)
    {
        var invoice = InvoiceWith("INV-HIGH", status, 500_000.01m);
        invoice.ApprovalHistory.Add(new InvoiceApproval("first.user", "Manager", DateTime.UtcNow));
        var before = Snapshot(invoice);
        var (service, repository) = ServiceFor(invoice);

        var exception = Assert.Throws<InvalidOperationException>(
            () => service.Approve("INV-HIGH", "second.director", "Director"));

        Assert.Contains("only pending invoices can be decided", exception.Message);
        AssertUnchanged(invoice, before, repository);
    }

    [Fact]
    public async Task Approve_ConcurrentFirstApprovalAttemptsCannotRecordDuplicateStage()
    {
        var invoice = InvoiceWith("INV-HIGH", InvoiceStatus.PendingApproval, 500_000.01m);
        invoice.DecidedBy = null;
        invoice.DecidedAt = null;
        var (service, repository) = ServiceFor(invoice);

        var attempts = Enumerable.Range(0, 2)
            .Select(_ => Task.Run(() => Record.Exception(
                () => service.Approve("INV-HIGH", "same.manager", "Manager"))))
            .ToArray();
        var attemptResults = await Task.WhenAll(attempts);

        Assert.Single(attemptResults, attempt => attempt is null);
        Assert.Single(attemptResults, attempt => attempt is InvalidOperationException);
        var persistedInvoice = repository.GetById("INV-HIGH")!;
        Assert.Single(persistedInvoice.ApprovalHistory);
        Assert.Equal(InvoiceStatus.PendingApproval, persistedInvoice.Status);
        Assert.Null(persistedInvoice.DecidedBy);
        Assert.Null(persistedInvoice.DecidedAt);
        Assert.Equal(1, repository.UpdateCount);
    }

    [Fact]
    public void UpdateAtomically_DiscardsPartialChangesWhenCallbackThrows()
    {
        var invoice = InvoiceWith("INV-ATOMIC", InvoiceStatus.PendingApproval, 600_000m);
        invoice.DecidedBy = null;
        invoice.DecidedAt = null;
        invoice.DecisionNote = null;
        invoice.ApprovalHistory.Add(new InvoiceApproval("first.user", "Manager", DateTime.UtcNow));
        var before = Snapshot(invoice);
        var repository = new FakeInvoiceRepository(invoice);

        Assert.Throws<InvalidOperationException>(() => repository.UpdateAtomically("INV-ATOMIC", workingInvoice =>
        {
            workingInvoice.Status = InvoiceStatus.Approved;
            workingInvoice.ApprovalHistory.Add(new InvoiceApproval("second.user", "Director", DateTime.UtcNow));
            workingInvoice.DecidedBy = "second.user";
            workingInvoice.DecidedAt = DateTime.UtcNow;
            throw new InvalidOperationException("Simulated validation failure.");
        }));

        AssertUnchanged(invoice, before, repository);
    }

    [Fact]
    public void InMemoryRepository_UpdateAtomicallyRollsBackPartialChangesWhenCallbackThrows()
    {
        var invoice = InvoiceWith("INV-PRODUCTION-ATOMIC", InvoiceStatus.PendingApproval, 600_000m);
        invoice.DecidedBy = null;
        invoice.DecidedAt = null;
        invoice.DecisionNote = null;
        invoice.ApprovalHistory.Add(new InvoiceApproval("first.user", "Manager", DateTime.UtcNow));
        var before = Snapshot(invoice);
        var repository = new InMemoryInvoiceRepository();
        var storedInvoice = repository.Add(invoice);

        Assert.Throws<InvalidOperationException>(() => repository.UpdateAtomically(storedInvoice.Id, workingInvoice =>
        {
            workingInvoice.Status = InvoiceStatus.Approved;
            workingInvoice.ApprovalHistory.Add(new InvoiceApproval("second.user", "Director", DateTime.UtcNow));
            workingInvoice.DecidedBy = "second.user";
            workingInvoice.DecidedAt = DateTime.UtcNow;
            throw new InvalidOperationException("Simulated validation failure.");
        }));

        var persistedInvoice = repository.GetById(storedInvoice.Id)!;
        AssertInvoiceMatches(before, persistedInvoice);
    }

    [Fact]
    public void Approve_At500000UsesNormalSingleApprovalFlow()
    {
        var invoice = InvoiceWith("INV-BOUNDARY", InvoiceStatus.PendingApproval, 500_000m);
        var (service, _) = ServiceFor(invoice);

        var result = service.Approve("INV-BOUNDARY", "director.user", "Director");

        Assert.Equal(InvoiceStatus.Approved, result.Status);
        Assert.Single(result.ApprovalHistory);
        Assert.Equal("director.user", result.DecidedBy);
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
    public void Reject_RequiresAReasonWithoutMutation()
    {
        var invoice = InvoiceWith("INV-1", InvoiceStatus.PendingApproval);
        var before = Snapshot(invoice);
        var (service, repository) = ServiceFor(invoice);

        Assert.Throws<ArgumentException>(() => service.Reject("INV-1", "demo.approver", "  "));
        AssertUnchanged(invoice, before, repository);
    }

    [Fact]
    public void Reject_RejectsNonPendingInvoiceWithoutMutation()
    {
        var invoice = InvoiceWith("INV-1", InvoiceStatus.Approved);
        var before = Snapshot(invoice);
        var (service, repository) = ServiceFor(invoice);

        Assert.Throws<InvalidOperationException>(
            () => service.Reject("INV-1", "demo.approver", "Invalid state"));

        AssertUnchanged(invoice, before, repository);
    }
}
