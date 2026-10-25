using InvoiceApp.Api.Auth;
using Xunit;

namespace InvoiceApp.Tests;

public class SessionStoreTests
{
    [Fact]
    public void DemoUser_UsesEmptyDefaultsForUnconfiguredCredentialsAndRole()
    {
        var user = new DemoUser();

        Assert.Equal(string.Empty, user.Username);
        Assert.Equal(string.Empty, user.Password);
        Assert.Equal(string.Empty, user.Role);
    }

    [Fact]
    public void Create_ReturnsUniqueTokenAndStoresSession()
    {
        var store = new SessionStore();
        var firstSession = new UserSession("manager.one", "Manager");
        var secondSession = new UserSession("director.one", "Director");

        var firstToken = store.Create(firstSession);
        var secondToken = store.Create(secondSession);

        Assert.False(string.IsNullOrWhiteSpace(firstToken));
        Assert.False(string.IsNullOrWhiteSpace(secondToken));
        Assert.NotEqual(firstToken, secondToken);
        Assert.Equal(firstSession, store.Get(firstToken));
        Assert.Equal(secondSession, store.Get(secondToken));
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("not-a-session-token")]
    public void Get_ReturnsNullForUnknownOrNullToken(string? token)
    {
        var store = new SessionStore();

        Assert.Null(store.Get(token));
    }

    [Fact]
    public void Remove_InvalidatesExistingTokenAndIsSafeForUnknownToken()
    {
        var store = new SessionStore();
        var token = store.Create(new UserSession("manager.one", "Manager"));

        store.Remove(token);
        store.Remove("unknown-token");

        Assert.Null(store.Get(token));
    }
}
