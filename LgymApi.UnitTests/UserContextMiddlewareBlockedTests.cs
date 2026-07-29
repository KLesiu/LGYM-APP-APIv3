using LgymApi.Api.Middleware;
using LgymApi.Domain.Security;
using LgymApi.Domain.ValueObjects;
using LgymApi.Identity.Contracts;
using LgymApi.Identity.Contracts.Accounts;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using System.Security.Claims;
using FluentAssertions;
using NSubstitute;

namespace LgymApi.UnitTests;

[TestFixture]
public sealed class UserContextMiddlewareBlockedTests
{
    private IAuthenticatedAccountContextResolver _authenticatedAccountContextResolver = null!;
    private UserContextMiddleware _middleware = null!;

    [SetUp]
    public void SetUp()
    {
        _authenticatedAccountContextResolver = Substitute.For<IAuthenticatedAccountContextResolver>();
        _middleware = new UserContextMiddleware(context => Task.CompletedTask);
    }

    [Test]
    public async Task InvokeAsync_Returns403_WhenUserIsBlocked()
    {
        var accountId = Id<AccountReference>.New();
        var sessionId = Id<AccountSessionReference>.New();
        _authenticatedAccountContextResolver
            .ResolveAsync(Arg.Any<Id<AccountReference>>(), Arg.Any<Id<AccountSessionReference>>(), CancellationToken.None)
            .Returns(new AuthenticatedAccountResolution(AuthenticatedAccountResolutionStatus.AccountBlocked, CreateContext(accountId, sessionId, isBlocked: true)));

        var context = CreateHttpContext(accountId, sessionId);

        await _middleware.InvokeAsync(context, _authenticatedAccountContextResolver);

        context.Response.StatusCode.Should().Be(StatusCodes.Status403Forbidden);
    }

    [Test]
    public async Task InvokeAsync_PassesThrough_WhenUserIsNotBlocked()
    {
        var accountId = Id<AccountReference>.New();
        var sessionId = Id<AccountSessionReference>.New();
        _authenticatedAccountContextResolver
            .ResolveAsync(Arg.Any<Id<AccountReference>>(), Arg.Any<Id<AccountSessionReference>>(), CancellationToken.None)
            .Returns(new AuthenticatedAccountResolution(AuthenticatedAccountResolutionStatus.Active, CreateContext(accountId, sessionId)));

        var nextCalled = false;
        var middleware = new UserContextMiddleware(_ => { nextCalled = true; return Task.CompletedTask; });
        var context = CreateHttpContext(accountId, sessionId);

        await middleware.InvokeAsync(context, _authenticatedAccountContextResolver);

        nextCalled.Should().BeTrue();
        context.Response.StatusCode.Should().Be(200);
        context.GetAuthenticatedAccountContext()!.Id.Should().Be(accountId);
        context.Items.Should().NotContainKey("User");
    }

    [Test]
    public async Task InvokeAsync_Returns401WithoutResolving_WhenSessionClaimIsMissing()
    {
        var context = new DefaultHttpContext { Response = { Body = new MemoryStream() } };
        context.User = new ClaimsPrincipal(new ClaimsIdentity([
            new Claim(AuthConstants.ClaimNames.UserId, Id<AccountReference>.New().ToString())]));
        context.SetEndpoint(new Endpoint(_ => Task.CompletedTask, EndpointMetadataCollection.Empty, "Test"));

        await _middleware.InvokeAsync(context, _authenticatedAccountContextResolver);

        context.Response.StatusCode.Should().Be(StatusCodes.Status401Unauthorized);
        await _authenticatedAccountContextResolver.DidNotReceiveWithAnyArgs()
            .ResolveAsync(default, default, default);
    }

    private static DefaultHttpContext CreateHttpContext(Id<AccountReference> accountId, Id<AccountSessionReference> sessionId)
    {
        var context = new DefaultHttpContext();
        context.User = new ClaimsPrincipal(new ClaimsIdentity(new[]
        {
            new Claim(AuthConstants.ClaimNames.UserId, accountId.ToString()),
            new Claim(AuthConstants.ClaimNames.SessionId, sessionId.ToString())
        }));
        context.Response.Body = new System.IO.MemoryStream();
        context.SetEndpoint(new Endpoint(
            _ => Task.CompletedTask,
            EndpointMetadataCollection.Empty,
            "Test"));
        return context;
    }

    private static AuthenticatedAccountContext CreateContext(
        Id<AccountReference> accountId,
        Id<AccountSessionReference> sessionId,
        bool isBlocked = false)
        => new(
            accountId,
            sessionId,
            [],
            [],
            isBlocked,
            false);
}
