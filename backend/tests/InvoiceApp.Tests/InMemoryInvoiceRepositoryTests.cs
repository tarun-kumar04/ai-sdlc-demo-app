using InvoiceApp.Api;
using InvoiceApp.Domain;
using Xunit;

namespace InvoiceApp.Tests;

public class InMemoryInvoiceRepositoryTests
{
    [Fact]
    public void Constructor_SeedsExpectedInvoices()
    {
        var repository = new InMemoryInvoiceRepository();

        var invoices = repository.GetAll();

        Assert.Collection(invoices,
            invoice => AssertInvoice(invoice, "INV-1001", "Lakeside School District", 4_250m, InvoiceStatus.PendingApproval),
            invoice => AssertInvoice(invoice, "INV-1002", "Hilltop Academy", 980.50m, InvoiceStatus.PendingApproval),
            invoice =>
            {
                AssertInvoice(invoice, "INV-1003", "Riverside Charter", 12_300m, InvoiceStatus.Approved);
                Assert.Equal("demo.approver", invoice.DecidedBy);
                Assert.NotNull(invoice.DecidedAt);
                Assert.True(invoice.DecidedAt < invoice.CreatedAt);
            },
            invoice => AssertInvoice(invoice, "INV-1004", "Oakwood Independent", 615.75m, InvoiceStatus.PendingApproval),
            invoice => AssertInvoice(invoice, "INV-1005", "Maple Grove Prep", 310m, InvoiceStatus.Draft));
    }

    [Fact]
    public void GetAll_ReturnsAllStoredInvoices()
    {
        var repository = new InMemoryInvoiceRepository();

        var invoices = repository.GetAll();

        Assert.Equal(5, invoices.Count);
        Assert.Equal(new[] { "INV-1001", "INV-1002", "INV-1003", "INV-1004", "INV-1005" },
            invoices.Select(invoice => invoice.Id));
    }

    [Fact]
    public void GetById_ReturnsMatchingInvoiceOrNull()
    {
        var repository = new InMemoryInvoiceRepository();

        var invoice = repository.GetById("INV-1003");

        Assert.NotNull(invoice);
        Assert.Equal("Riverside Charter", invoice.CustomerName);
        Assert.Null(repository.GetById("INV-404"));
    }

    [Fact]
    public void GetByIds_ReturnsOnlyRequestedInvoicesInSeedOrder()
    {
        var repository = new InMemoryInvoiceRepository();

        var invoices = repository.GetByIds(new[] { "INV-1004", "INV-1001", "INV-404" });

        Assert.Equal(new[] { "INV-1001", "INV-1004" }, invoices.Select(invoice => invoice.Id));
        Assert.Empty(repository.GetByIds(new[] { "INV-404" }));
    }

    [Fact]
    public void Add_AssignsSequentialIdsWhenMissing()
    {
        var repository = new InMemoryInvoiceRepository();
        var first = repository.Add(new Invoice { CustomerName = "New customer", Amount = 10m });
        var second = repository.Add(new Invoice { CustomerName = "Another customer", Amount = 20m });

        Assert.Equal("INV-1006", first.Id);
        Assert.Equal("INV-1007", second.Id);
        Assert.Same(first, repository.GetById(first.Id));
        Assert.Same(second, repository.GetById(second.Id));
    }

    [Theory]
    [InlineData("INV-1001")]
    [InlineData("INV-1005")]
    public void Update_ReplacesMatchingInvoiceWithoutChangingOtherEntries(string invoiceId)
    {
        var repository = new InMemoryInvoiceRepository();
        var before = repository.GetAll().ToDictionary(invoice => invoice.Id, CopyInvoice);
        var updatedInvoice = CopyInvoice(repository.GetById(invoiceId)!);
        updatedInvoice.CustomerName = "Updated customer";
        updatedInvoice.Amount = 45_678.90m;
        updatedInvoice.Status = InvoiceStatus.Rejected;
        updatedInvoice.DecidedBy = "manager.user";
        updatedInvoice.DecidedAt = new DateTime(2026, 1, 2, 3, 4, 5, DateTimeKind.Utc);
        updatedInvoice.DecisionNote = "Review required";
        updatedInvoice.ApprovalHistory.Add(
            new InvoiceApproval("manager.user", "Manager", updatedInvoice.DecidedAt.Value));

        repository.Update(updatedInvoice);

        AssertInvoiceState(updatedInvoice, repository.GetById(invoiceId)!);
        Assert.Equal(before.Count, repository.GetAll().Count);
        foreach (var untouchedInvoice in before.Where(pair => pair.Key != invoiceId))
        {
            AssertInvoiceState(untouchedInvoice.Value, repository.GetById(untouchedInvoice.Key)!);
        }
    }

    [Fact]
    public void Update_UnknownInvoiceDoesNotChangeStoredInvoices()
    {
        var repository = new InMemoryInvoiceRepository();
        var before = repository.GetAll().Select(CopyInvoice).ToArray();

        repository.Update(new Invoice { Id = "INV-404", CustomerName = "Unknown", Amount = 1m });

        Assert.Equal(before.Length, repository.GetAll().Count);
        foreach (var original in before)
        {
            AssertInvoiceState(original, repository.GetById(original.Id)!);
        }
    }

    [Theory]
    [InlineData("INV-1001")]
    [InlineData("INV-1005")]
    public void UpdateAtomically_UpdatesTargetAndPreservesAllInvoiceFieldsAndHistory(string invoiceId)
    {
        var repository = new InMemoryInvoiceRepository();
        var before = repository.GetAll().ToDictionary(invoice => invoice.Id, CopyInvoice);
        var result = repository.UpdateAtomically(invoiceId, invoice =>
        {
            invoice.CustomerName = "Atomic update";
            invoice.Amount = 12_345.67m;
            invoice.Status = InvoiceStatus.PendingApproval;
            invoice.CreatedAt = new DateTime(2026, 2, 3, 4, 5, 6, DateTimeKind.Utc);
            invoice.ApprovalHistory.Add(
                new InvoiceApproval("director.user", "Director", new DateTime(2026, 2, 4, 5, 6, 7, DateTimeKind.Utc)));
            invoice.DecidedBy = "director.user";
            invoice.DecidedAt = new DateTime(2026, 2, 4, 5, 6, 7, DateTimeKind.Utc);
            invoice.DecisionNote = "Decision recorded";
        });

        var persisted = repository.GetById(invoiceId)!;
        Assert.Same(persisted, result);
        Assert.Equal("Atomic update", persisted.CustomerName);
        Assert.Equal(12_345.67m, persisted.Amount);
        Assert.Equal(InvoiceStatus.PendingApproval, persisted.Status);
        Assert.Equal(new DateTime(2026, 2, 3, 4, 5, 6, DateTimeKind.Utc), persisted.CreatedAt);
        Assert.Equal("director.user", persisted.DecidedBy);
        Assert.Equal(new DateTime(2026, 2, 4, 5, 6, 7, DateTimeKind.Utc), persisted.DecidedAt);
        Assert.Equal("Decision recorded", persisted.DecisionNote);
        Assert.Equal("director.user", Assert.Single(persisted.ApprovalHistory).Approver);

        foreach (var untouchedInvoice in before.Where(pair => pair.Key != invoiceId))
        {
            AssertInvoiceState(untouchedInvoice.Value, repository.GetById(untouchedInvoice.Key)!);
        }
    }

    [Fact]
    public void UpdateAtomically_ThrowsForUnknownIdWithoutChangingStoredInvoices()
    {
        var repository = new InMemoryInvoiceRepository();
        var before = repository.GetAll().Select(CopyInvoice).ToArray();

        Assert.Throws<KeyNotFoundException>(() => repository.UpdateAtomically("INV-404", _ => { }));

        Assert.Equal(before.Length, repository.GetAll().Count);
        foreach (var original in before)
        {
            AssertInvoiceState(original, repository.GetById(original.Id)!);
        }
    }

    [Fact]
    public void UpdateAtomically_DoesNotCommitWhenCallbackThrows()
    {
        var repository = new InMemoryInvoiceRepository();
        var before = CopyInvoice(repository.GetById("INV-1001")!);

        Assert.Throws<InvalidOperationException>(() => repository.UpdateAtomically("INV-1001", invoice =>
        {
            invoice.Status = InvoiceStatus.Approved;
            invoice.CustomerName = "Partial mutation";
            invoice.ApprovalHistory.Add(new InvoiceApproval("director.user", "Director", DateTime.UtcNow));
            invoice.DecidedBy = "director.user";
            throw new InvalidOperationException("Reject update.");
        }));

        AssertInvoiceState(before, repository.GetById("INV-1001")!);
    }

    [Fact]
    public void Remove_RemovesExistingInvoiceAndReturnsFalseForUnknownId()
    {
        var repository = new InMemoryInvoiceRepository();

        Assert.True(repository.Remove("INV-1002"));
        Assert.Null(repository.GetById("INV-1002"));
        Assert.Equal(4, repository.GetAll().Count);
        Assert.False(repository.Remove("INV-404"));
        Assert.Equal(4, repository.GetAll().Count);
    }

    private static void AssertInvoice(Invoice invoice, string id, string customerName, decimal amount, InvoiceStatus status)
    {
        Assert.Equal(id, invoice.Id);
        Assert.Equal(customerName, invoice.CustomerName);
        Assert.Equal(amount, invoice.Amount);
        Assert.Equal(status, invoice.Status);
    }

    private static Invoice CopyInvoice(Invoice invoice) => new()
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

    private static void AssertInvoiceState(Invoice expected, Invoice actual)
    {
        Assert.Equal(expected.Id, actual.Id);
        Assert.Equal(expected.CustomerName, actual.CustomerName);
        Assert.Equal(expected.Amount, actual.Amount);
        Assert.Equal(expected.Status, actual.Status);
        Assert.Equal(expected.CreatedAt, actual.CreatedAt);
        Assert.Equal(expected.ApprovalHistory, actual.ApprovalHistory);
        Assert.Equal(expected.DecidedBy, actual.DecidedBy);
        Assert.Equal(expected.DecidedAt, actual.DecidedAt);
        Assert.Equal(expected.DecisionNote, actual.DecisionNote);
    }
}
