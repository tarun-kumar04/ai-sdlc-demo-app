using InvoiceApp.Api.Auth;
using InvoiceApp.Api.Controllers;
using InvoiceApp.Domain;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Xunit;

namespace InvoiceApp.Tests;

public class ApprovalsControllerTests
{
    [Fact]
    public void Approve_PassesAuthenticatedSessionIdentityAndRoleToDomain()
    {
        var invoice = new Invoice
        {
            Id = "INV-HIGH",
            Amount = 600_000m,
            Status = InvoiceStatus.PendingApproval,
            DecidedBy = null,
            DecidedAt = null,
            DecisionNote = null
        };
        var repository = new FakeInvoiceRepository(invoice);
        var controller = new ApprovalsController(new ApprovalService(repository));
        controller.ControllerContext = new ControllerContext { HttpContext = new DefaultHttpContext() };
        controller.HttpContext.Items[HttpContextExtensions.SessionKey] = new UserSession("session.manager", "Manager");

        var result = controller.Approve("INV-HIGH");

        var approvedInvoice = Assert.IsType<Invoice>(Assert.IsType<OkObjectResult>(result.Result).Value);
        Assert.Equal(InvoiceStatus.PendingApproval, approvedInvoice.Status);
        var approval = Assert.Single(approvedInvoice.ApprovalHistory);
        Assert.Equal("session.manager", approval.Approver);
        Assert.Equal("Manager", approval.Role);
    }

    [Fact]
    public void Reject_UsesAuthenticatedSessionIdentity()
    {
        var invoice = new Invoice { Id = "INV-1", Status = InvoiceStatus.PendingApproval };
        var repository = new FakeInvoiceRepository(invoice);
        var controller = new ApprovalsController(new ApprovalService(repository));
        controller.ControllerContext = new ControllerContext { HttpContext = new DefaultHttpContext() };
        controller.HttpContext.Items[HttpContextExtensions.SessionKey] = new UserSession("session.director", "Director");

        var result = controller.Reject("INV-1", new ApprovalsController.RejectRequest("Duplicate"));

        var rejectedInvoice = Assert.IsType<Invoice>(Assert.IsType<OkObjectResult>(result.Result).Value);
        Assert.Equal("session.director", rejectedInvoice.DecidedBy);
        Assert.Equal("Duplicate", rejectedInvoice.DecisionNote);
    }
}
