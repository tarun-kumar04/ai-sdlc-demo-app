using InvoiceApp.Api.Auth;
using InvoiceApp.Api.Controllers;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Options;
using Xunit;

namespace InvoiceApp.Tests;

public class AuthControllerTests
{
    [Fact]
    public void Login_ReturnsConfiguredSessionAndRoleForValidCredentials()
    {
        var users = new DemoUsersOptions
        {
            Users =
            {
                new DemoUser { Username = "demo.manager", Password = "test-password", Role = "Manager" }
            }
        };
        var sessions = new SessionStore();
        var controller = new AuthController(Options.Create(users), sessions);

        var actionResult = controller.Login(new AuthController.LoginRequest("demo.manager", "test-password"));
        var response = Assert.IsType<AuthController.LoginResponse>(Assert.IsType<OkObjectResult>(actionResult.Result).Value);

        Assert.Equal("demo.manager", response.Username);
        Assert.Equal("Manager", response.Role);
        Assert.Equal(new UserSession("demo.manager", "Manager"), sessions.Get(response.Token));
    }

    [Theory]
    [InlineData("unknown.user", "test-password")]
    [InlineData("demo.manager", "wrong-password")]
    public void Login_RejectsUnknownUsernameOrIncorrectPassword(string username, string password)
    {
        var users = new DemoUsersOptions
        {
            Users =
            {
                new DemoUser { Username = "demo.manager", Password = "test-password", Role = "Manager" }
            }
        };
        var controller = new AuthController(Options.Create(users), new SessionStore());

        var result = controller.Login(new AuthController.LoginRequest(username, password));

        var unauthorized = Assert.IsType<UnauthorizedObjectResult>(result.Result);
        Assert.Contains("Invalid username or password", unauthorized.Value!.ToString());
    }

    [Fact]
    public void Logout_RemovesBearerSession()
    {
        var sessions = new SessionStore();
        var token = sessions.Create(new UserSession("demo.manager", "Manager"));
        var controller = new AuthController(
            Options.Create(new DemoUsersOptions()), sessions);
        controller.ControllerContext = new ControllerContext
        {
            HttpContext = new DefaultHttpContext()
        };
        controller.Request.Headers.Authorization = $"Bearer {token}";

        var result = controller.Logout();

        Assert.IsType<NoContentResult>(result);
        Assert.Null(sessions.Get(token));
    }

    [Fact]
    public void Logout_WithoutBearerTokenReturnsNoContent()
    {
        var controller = new AuthController(
            Options.Create(new DemoUsersOptions()), new SessionStore());
        controller.ControllerContext = new ControllerContext
        {
            HttpContext = new DefaultHttpContext()
        };

        Assert.IsType<NoContentResult>(controller.Logout());
    }
}
