using LgymApi.Api.Hubs;
using LgymApi.Domain.Security;
using LgymApi.Domain.ValueObjects;
using LgymApi.Identity.Contracts;
using LgymApi.Identity.Contracts.Accounts;
using Microsoft.AspNetCore.Http.Features;
using Microsoft.AspNetCore.SignalR;
using Microsoft.Extensions.Logging.Abstractions;
using NSubstitute;
using System.Security.Claims;
using FluentAssertions;

namespace LgymApi.UnitTests.InAppNotifications;

[TestFixture]
public sealed class NotificationHubTests
{
    [Test]
    public async Task OnConnectedAsync_ActiveAccount_AddsAccountToGroup()
    {
        var accountId = Id<AccountReference>.New();
        var sessionId = Id<AccountSessionReference>.New();
        var resolver = CreateResolver(accountId, sessionId, AuthenticatedAccountResolutionStatus.Active);
        var context = new TestHubCallerContext(accountId.ToString(), sessionId.ToString());
        var groups = new FakeGroupManager();

        var hub = new NotificationHub(resolver, new AccountSessionConnectionRegistry(), NullLogger<NotificationHub>.Instance)
        {
            Context = context,
            Groups = groups
        };

        await hub.OnConnectedAsync();

        context.AbortCalled.Should().BeFalse();
        groups.AddCalls.Should().Be(1);
        groups.LastGroupName.Should().Be($"user-{accountId}");
        await resolver.Received(1).ResolveAsync(accountId, sessionId, CancellationToken.None);
    }

    [Test]
    public async Task OnConnectedAsync_MissingSessionId_AbortsConnection()
    {
        var resolver = Substitute.For<IAuthenticatedAccountContextResolver>();
        var context = new TestHubCallerContext(Id<AccountReference>.New().ToString(), null);
        var groups = new FakeGroupManager();

        var hub = new NotificationHub(resolver, new AccountSessionConnectionRegistry(), NullLogger<NotificationHub>.Instance)
        {
            Context = context,
            Groups = groups
        };

        await hub.OnConnectedAsync();

        context.AbortCalled.Should().BeTrue();
        groups.AddCalls.Should().Be(0);
        await resolver.DidNotReceiveWithAnyArgs().ResolveAsync(default, default, default);
    }

    [Test]
    public async Task OnConnectedAsync_InvalidSession_AbortsConnection()
    {
        var accountId = Id<AccountReference>.New();
        var invalidSessionId = Id<AccountSessionReference>.New();
        var resolver = CreateResolver(accountId, invalidSessionId, AuthenticatedAccountResolutionStatus.SessionInvalid);
        var context = new TestHubCallerContext(accountId.ToString(), invalidSessionId.ToString());
        var groups = new FakeGroupManager();

        var hub = new NotificationHub(resolver, new AccountSessionConnectionRegistry(), NullLogger<NotificationHub>.Instance)
        {
            Context = context,
            Groups = groups
        };

        await hub.OnConnectedAsync();

        context.AbortCalled.Should().BeTrue();
        groups.AddCalls.Should().Be(0);
        await resolver.Received(1).ResolveAsync(accountId, invalidSessionId, CancellationToken.None);
    }

    private static IAuthenticatedAccountContextResolver CreateResolver(
        Id<AccountReference> accountId,
        Id<AccountSessionReference> sessionId,
        AuthenticatedAccountResolutionStatus status)
    {
        var resolver = Substitute.For<IAuthenticatedAccountContextResolver>();
        var context = status == AuthenticatedAccountResolutionStatus.Active
            ? new AuthenticatedAccountContext(accountId, sessionId, [], [], false, false)
            : null;
        resolver.ResolveAsync(accountId, sessionId, CancellationToken.None)
            .Returns(Task.FromResult(new AuthenticatedAccountResolution(status, context)));
        return resolver;
    }

    private sealed class FakeGroupManager : IGroupManager
    {
        public int AddCalls { get; private set; }
        public string? LastGroupName { get; private set; }

        public Task AddToGroupAsync(string connectionId, string groupName, CancellationToken cancellationToken = default)
        {
            AddCalls++;
            LastGroupName = groupName;
            return Task.CompletedTask;
        }

        public Task RemoveFromGroupAsync(string connectionId, string groupName, CancellationToken cancellationToken = default)
            => Task.CompletedTask;
    }

    private sealed class TestHubCallerContext : HubCallerContext
    {
        public TestHubCallerContext(string? userId, string? sessionId)
        {
            var claims = new List<Claim>();

            if (userId != null)
            {
                claims.Add(new Claim(AuthConstants.ClaimNames.UserId, userId));
            }

            if (sessionId != null)
            {
                claims.Add(new Claim(AuthConstants.ClaimNames.SessionId, sessionId));
            }

            User = claims.Count > 0
                ? new ClaimsPrincipal(new ClaimsIdentity(claims, "Test"))
                : new ClaimsPrincipal(new ClaimsIdentity());
        }

        public bool AbortCalled { get; private set; }

        public override string ConnectionId { get; } = "connection-1";
        public override string? UserIdentifier => null;
        public override ClaimsPrincipal? User { get; }
        public override IDictionary<object, object?> Items { get; } = new Dictionary<object, object?>();
        public override IFeatureCollection Features { get; } = new FeatureCollection();
        public override CancellationToken ConnectionAborted => CancellationToken.None;
        public override void Abort() => AbortCalled = true;
    }
}
