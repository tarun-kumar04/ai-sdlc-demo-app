using InvoiceApp.Api.Auth;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Abstractions;
using Microsoft.AspNetCore.Mvc.Filters;
using Microsoft.AspNetCore.Routing;
using Xunit;

namespace InvoiceApp.Tests;

public class RequireRoleAttributeTests
{
    [Theory]
    [InlineData("Manager")]
    [InlineData("Director")]
    public void OnActionExecuting_AllowsConfiguredRole(string role)
    {
        var context = CreateContext(new UserSession("user.one", role));
        var attribute = new RequireRoleAttribute("Manager", "Director");

        attribute.OnActionExecuting(context);

        Assert.Null(context.Result);
    }

    [Fact]
    public void OnActionExecuting_RejectsDisallowedRole()
    {
        var context = CreateContext(new UserSession("user.one", "Viewer"));
        var attribute = new RequireRoleAttribute("Manager", "Director");

        attribute.OnActionExecuting(context);

        Assert.IsType<StatusCodeResult>(context.Result);
        Assert.Equal(StatusCodes.Status403Forbidden, ((StatusCodeResult)context.Result).StatusCode);
    }

    [Fact]
    public void OnActionExecuting_RejectsMissingSession()
    {
        var context = CreateContext(null);
        var attribute = new RequireRoleAttribute("Manager", "Director");

        attribute.OnActionExecuting(context);

        Assert.IsType<StatusCodeResult>(context.Result);
        Assert.Equal(StatusCodes.Status403Forbidden, ((StatusCodeResult)context.Result).StatusCode);
    }

    private static ActionExecutingContext CreateContext(UserSession? session)
    {
        var httpContext = new DefaultHttpContext();
        if (session is not null)
        {
            httpContext.Items[HttpContextExtensions.SessionKey] = session;
        }

        return new ActionExecutingContext(
            new ActionContext(httpContext, new RouteData(), new ActionDescriptor()),
            new List<IFilterMetadata>(),
            new Dictionary<string, object?>(),
            new object());
    }
}
