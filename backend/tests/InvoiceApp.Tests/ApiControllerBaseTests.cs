using InvoiceApp.Api.Controllers;
using InvoiceApp.Domain;
using Microsoft.AspNetCore.Mvc;
using Xunit;

namespace InvoiceApp.Tests;

public class ApiControllerBaseTests
{
    private readonly ExposedApiController _controller = new();

    [Fact]
    public void Handle_ReturnsOkForSuccessfulOperation()
    {
        var invoice = new Invoice { Id = "INV-1", CustomerName = "School", Amount = 100m };

        var result = _controller.InvokeHandle(() => invoice);

        var okResult = Assert.IsType<OkObjectResult>(result.Result);
        Assert.Same(invoice, okResult.Value);
    }

    [Fact]
    public void Handle_MapsMissingInvoiceToNotFoundWithMessage()
    {
        var result = _controller.InvokeHandle(() => throw new KeyNotFoundException("Invoice INV-404 was not found."));

        var notFound = Assert.IsType<NotFoundObjectResult>(result.Result);
        Assert.Contains("Invoice INV-404 was not found", notFound.Value!.ToString());
    }

    [Fact]
    public void Handle_MapsInvalidOperationToConflictWithMessage()
    {
        var result = _controller.InvokeHandle(() => throw new InvalidOperationException("Approval stage is invalid."));

        var conflict = Assert.IsType<ConflictObjectResult>(result.Result);
        Assert.Contains("Approval stage is invalid", conflict.Value!.ToString());
    }

    [Fact]
    public void Handle_MapsArgumentErrorToBadRequestWithMessage()
    {
        var result = _controller.InvokeHandle(() => throw new ArgumentException("A reason is required."));

        var badRequest = Assert.IsType<BadRequestObjectResult>(result.Result);
        Assert.Contains("A reason is required", badRequest.Value!.ToString());
    }

    private sealed class ExposedApiController : ApiControllerBase
    {
        public ActionResult<Invoice> InvokeHandle(Func<Invoice> operation) => Handle(operation);
    }
}
