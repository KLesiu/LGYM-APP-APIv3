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
using LgymApi.Api.AgeGate;
using LgymApi.Identity.Contracts.AdultConfirmation;
using Microsoft.Extensions.Options;
using System.Text.Json;

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

        await _middleware.InvokeAsync(
            context,
            _authenticatedAccountContextResolver,
            Options.Create(new AgeGateOptions { Enabled = true }));

        context.Response.StatusCode.Should().Be(StatusCodes.Status403Forbidden);
    }

    [Test]
    public async Task InvokeAsync_ReturnsStable428_WhenAdultConfirmationIsMissingAndGateEnabled()
    {
        var accountId = Id<AccountReference>.New();
        var sessionId = Id<AccountSessionReference>.New();
        _authenticatedAccountContextResolver
            .ResolveAsync(Arg.Any<Id<AccountReference>>(), Arg.Any<Id<AccountSessionReference>>(), CancellationToken.None)
            .Returns(new AuthenticatedAccountResolution(
                AuthenticatedAccountResolutionStatus.Active,
                CreateContext(accountId, sessionId)));
        var nextCalled = false;
        var middleware = new UserContextMiddleware(_ => { nextCalled = true; return Task.CompletedTask; });
        var context = CreateHttpContext(accountId, sessionId);

        await middleware.InvokeAsync(
            context,
            _authenticatedAccountContextResolver,
            Options.Create(new AgeGateOptions { Enabled = true }));

        nextCalled.Should().BeFalse();
        context.Response.StatusCode.Should().Be(StatusCodes.Status428PreconditionRequired);
        context.Response.Body.Position = 0;
        using var payload = await JsonDocument.ParseAsync(context.Response.Body);
        payload.RootElement.GetProperty("code").GetString().Should().Be("AdultConfirmationRequired");
    }

    [Test]
    public async Task InvokeAsync_PassesThroughAllowlistedEndpoint_WhenAdultConfirmationIsMissing()
    {
        var accountId = Id<AccountReference>.New();
        var sessionId = Id<AccountSessionReference>.New();
        _authenticatedAccountContextResolver
            .ResolveAsync(Arg.Any<Id<AccountReference>>(), Arg.Any<Id<AccountSessionReference>>(), CancellationToken.None)
            .Returns(new AuthenticatedAccountResolution(
                AuthenticatedAccountResolutionStatus.Active,
                CreateContext(accountId, sessionId)));
        var nextCalled = false;
        var middleware = new UserContextMiddleware(_ => { nextCalled = true; return Task.CompletedTask; });
        var context = CreateHttpContext(accountId, sessionId, new AllowAgeGatedAttribute());

        await middleware.InvokeAsync(
            context,
            _authenticatedAccountContextResolver,
            Options.Create(new AgeGateOptions { Enabled = true }));

        nextCalled.Should().BeTrue();
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

        await middleware.InvokeAsync(
            context,
            _authenticatedAccountContextResolver,
            Options.Create(new AgeGateOptions()));

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

        await _middleware.InvokeAsync(
            context,
            _authenticatedAccountContextResolver,
            Options.Create(new AgeGateOptions()));

        context.Response.StatusCode.Should().Be(StatusCodes.Status401Unauthorized);
        await _authenticatedAccountContextResolver.DidNotReceiveWithAnyArgs()
            .ResolveAsync(default, default, default);
    }

    private static DefaultHttpContext CreateHttpContext(
        Id<AccountReference> accountId,
        Id<AccountSessionReference> sessionId,
        params object[] metadata)
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
            new EndpointMetadataCollection(metadata),
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
