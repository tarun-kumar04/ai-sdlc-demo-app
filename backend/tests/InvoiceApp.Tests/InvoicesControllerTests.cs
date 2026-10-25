using InvoiceApp.Api.Auth;
using InvoiceApp.Api.Controllers;
using InvoiceApp.Domain;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Xunit;

namespace InvoiceApp.Tests;

public class InvoicesControllerTests
{
    [Fact]
    public void GetAll_ReturnsInvoicesWhenNoStatusFilterIsProvided()
    {
        var repository = new FakeInvoiceRepository(
            new Invoice { Id = "INV-1", Status = InvoiceStatus.Draft },
            new Invoice { Id = "INV-2", Status = InvoiceStatus.Approved });
        var controller = CreateController(repository);

        var result = controller.GetAll(null);

        var invoices = Assert.IsType<OkObjectResult>(result.Result).Value as IReadOnlyList<Invoice>;
        Assert.NotNull(invoices);
        Assert.Equal(new[] { "INV-1", "INV-2" }, invoices.Select(invoice => invoice.Id));
    }

    [Fact]
    public void GetAll_FiltersByStatusCaseInsensitively()
    {
        var repository = new FakeInvoiceRepository(
            new Invoice { Id = "INV-1", Status = InvoiceStatus.PendingApproval },
            new Invoice { Id = "INV-2", Status = InvoiceStatus.Approved });
        var controller = CreateController(repository);

        var result = controller.GetAll("approved");

        var invoices = Assert.IsType<OkObjectResult>(result.Result).Value as IReadOnlyList<Invoice>;
        Assert.NotNull(invoices);
        Assert.Equal(new[] { "INV-2" }, invoices.Select(invoice => invoice.Id));
    }

    [Fact]
    public void GetAll_RejectsUnknownStatus()
    {
        var controller = CreateController(new FakeInvoiceRepository());

        var result = controller.GetAll("UnknownStatus");

        var badRequest = Assert.IsType<BadRequestObjectResult>(result.Result);
        Assert.Contains("Unknown status 'UnknownStatus'", badRequest.Value!.ToString());
    }

    [Fact]
    public void GetById_ReturnsInvoiceOrNotFound()
    {
        var invoice = new Invoice { Id = "INV-1", CustomerName = "School", Amount = 250m };
        var controller = CreateController(new FakeInvoiceRepository(invoice));

        Assert.Same(invoice, Assert.IsType<OkObjectResult>(controller.GetById("INV-1").Result).Value);
        Assert.IsType<NotFoundResult>(controller.GetById("INV-404").Result);
    }

    [Theory]
    [InlineData("   ", 100)]
    [InlineData("School", 0)]
    [InlineData("School", -1)]
    public void Create_RejectsInvalidCustomerOrAmountWithoutAddingInvoice(string customerName, decimal amount)
    {
        var repository = new FakeInvoiceRepository();
        var controller = CreateController(repository);

        var result = controller.Create(new InvoicesController.CreateInvoiceRequest(customerName, amount));

        Assert.IsType<BadRequestObjectResult>(result.Result);
        Assert.Empty(repository.GetAll());
    }

    [Fact]
    public void Create_TrimsCustomerNameAndReturnsCreatedInvoice()
    {
        var repository = new FakeInvoiceRepository();
        var controller = CreateController(repository);

        var result = controller.Create(new InvoicesController.CreateInvoiceRequest("  School  ", 125m));

        var created = Assert.IsType<CreatedAtActionResult>(result.Result);
        var invoice = Assert.IsType<Invoice>(created.Value);
        Assert.Equal("School", invoice.CustomerName);
        Assert.Equal(125m, invoice.Amount);
        Assert.Equal(InvoiceStatus.Draft, invoice.Status);
        Assert.Same(invoice, repository.GetById(invoice.Id));
    }

    [Fact]
    public void Submit_MapsMissingAndInvalidStateToHttpErrors()
    {
        var repository = new FakeInvoiceRepository(new Invoice { Id = "INV-DONE", Status = InvoiceStatus.Approved });
        var controller = CreateController(repository);

        Assert.IsType<NotFoundObjectResult>(controller.Submit("INV-MISSING").Result);
        Assert.IsType<ConflictObjectResult>(controller.Submit("INV-DONE").Result);
    }

    [Fact]
    public void Delete_DelegatesToRepositoryAndReturnsNotFoundWhenMissing()
    {
        var repository = new FakeInvoiceRepository(new Invoice { Id = "INV-1" });
        var controller = CreateController(repository);

        Assert.IsType<NoContentResult>(controller.Delete("INV-1"));
        Assert.Null(repository.GetById("INV-1"));
        Assert.IsType<NotFoundResult>(controller.Delete("INV-404"));
    }

    private static InvoicesController CreateController(FakeInvoiceRepository repository)
    {
        var controller = new InvoicesController(repository, new ApprovalService(repository));
        controller.ControllerContext = new ControllerContext { HttpContext = new DefaultHttpContext() };
        controller.HttpContext.Items[HttpContextExtensions.SessionKey] = new UserSession("test.user", "Manager");
        return controller;
    }
}
