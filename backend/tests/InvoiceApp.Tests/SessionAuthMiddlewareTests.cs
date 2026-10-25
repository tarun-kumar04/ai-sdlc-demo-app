using InvoiceApp.Api.Auth;
using Microsoft.AspNetCore.Http;
using Xunit;

namespace InvoiceApp.Tests;

public class SessionAuthMiddlewareTests
{
    [Theory]
    [InlineData("/api/invoices")]
    [InlineData("/api/auth/logout")]
    public async Task InvokeAsync_AttachesSessionForValidBearerToken(string path)
    {
        var sessions = new SessionStore();
        var expected = new UserSession("manager.one", "Manager");
        var token = sessions.Create(expected);
        var middleware = new SessionAuthMiddleware(context =>
        {
            Assert.Equal(expected, context.GetSession());
            return Task.CompletedTask;
        }, sessions);
        var context = new DefaultHttpContext();
        context.Request.Path = path;
        context.Request.Headers.Authorization = $"Bearer {token}";

        await middleware.InvokeAsync(context);

        Assert.Equal(StatusCodes.Status200OK, context.Response.StatusCode);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("invalid-token")]
    [InlineData("bearer token-with-wrong-case")]
    public async Task InvokeAsync_RejectsMissingOrInvalidBearerToken(string? authorization)
    {
        var invokedNext = false;
        var middleware = new SessionAuthMiddleware(_ =>
        {
            invokedNext = true;
            return Task.CompletedTask;
        }, new SessionStore());
        var context = new DefaultHttpContext();
        context.Request.Path = "/api/invoices";
        if (authorization is not null)
        {
            context.Request.Headers.Authorization = authorization;
        }

        await middleware.InvokeAsync(context);

        Assert.Equal(StatusCodes.Status401Unauthorized, context.Response.StatusCode);
        Assert.False(invokedNext);
    }

    [Fact]
    public async Task InvokeAsync_AllowsLoginPathWithoutSession()
    {
        var invokedNext = false;
        var middleware = new SessionAuthMiddleware(_ =>
        {
            invokedNext = true;
            return Task.CompletedTask;
        }, new SessionStore());
        var context = new DefaultHttpContext();
        context.Request.Path = "/api/auth/login";

        await middleware.InvokeAsync(context);

        Assert.True(invokedNext);
        Assert.Equal(StatusCodes.Status200OK, context.Response.StatusCode);
        Assert.Null(context.Items[HttpContextExtensions.SessionKey]);
    }

    [Theory]
    [InlineData("/")]
    [InlineData("/health")]
    public async Task InvokeAsync_AllowsNonApiPathWithoutSession(string path)
    {
        var invokedNext = false;
        var middleware = new SessionAuthMiddleware(_ =>
        {
            invokedNext = true;
            return Task.CompletedTask;
        }, new SessionStore());
        var context = new DefaultHttpContext();
        context.Request.Path = path;

        await middleware.InvokeAsync(context);

        Assert.True(invokedNext);
        Assert.Equal(StatusCodes.Status200OK, context.Response.StatusCode);
        Assert.Null(context.Items[HttpContextExtensions.SessionKey]);
    }
}
