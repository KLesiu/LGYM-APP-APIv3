using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using System.Text.Json.Serialization;
using FluentAssertions;
using LgymApi.Api.Hubs;
using LgymApi.Application.Notifications;
using LgymApi.Application.Notifications.Models;
using LgymApi.Domain.Entities;
using LgymApi.Domain.Notifications;
using LgymApi.Domain.ValueObjects;
using LgymApi.Identity.Contracts;
using Microsoft.AspNetCore.Http.Connections;
using Microsoft.AspNetCore.SignalR.Client;
using Microsoft.Extensions.DependencyInjection;

namespace LgymApi.IntegrationTests.InAppNotifications;

[TestFixture]
public sealed class NotificationHubConnectionTests : IntegrationTestBase
{
    [Test]
    public async Task Hub_Unauthenticated_Returns401()
    {
        ClearAuthorizationHeader();

        var response = await Client.PostAsync("/hubs/notifications/negotiate?negotiateVersion=1", null);

        response.StatusCode.Should().Be(HttpStatusCode.Unauthorized);
    }

    [Test]
    public async Task Hub_AuthenticatedUser_CanConnect()
    {
        var user = await SeedUserAsync(name: "hub-user", email: "hub-user@example.com");
        SetAuthorizationHeader(user.Id);

        var response = await Client.PostAsync("/hubs/notifications/negotiate?negotiateVersion=1", null);

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        var body = await response.Content.ReadFromJsonAsync<NegotiateResponse>();
        body.Should().NotBeNull();
        body!.ConnectionId.Should().NotBeNullOrWhiteSpace();
        body.AvailableTransports.Should().NotBeNull();
        body.AvailableTransports.Should().NotBeEmpty();
    }

    [Test]
    public async Task Hub_PushesOnlyToTheValidatedRecipientSession()
    {
        var recipient = await SeedUserAsync(name: "hub-recipient", email: "hub-recipient@example.com");
        var otherUser = await SeedUserAsync(name: "hub-other", email: "hub-other@example.com");
        var recipientSession = await CreateSessionAsync(recipient.Id);
        var otherSession = await CreateSessionAsync(otherUser.Id);

        await using var recipientConnection = CreateHubConnection(recipientSession.Token);
        await using var otherConnection = CreateHubConnection(otherSession.Token);
        var recipientNotification = ReceiveNotification(recipientConnection);
        var otherNotification = ReceiveNotification(otherConnection);

        await recipientConnection.StartAsync();
        await otherConnection.StartAsync();

        await PublishAsync(recipient.Id, "recipient-only");

        var received = await recipientNotification.Task.WaitAsync(TimeSpan.FromSeconds(2));
        received.GetProperty("message").GetString().Should().Be("recipient-only");
        await AssertNotReceivedAsync(otherNotification.Task);
    }

    [Test]
    public async Task Hub_LogoutRevocation_PrunesTerminatedSessionAndPreservesOtherDelivery()
    {
        var terminatedUser = await SeedUserAsync(name: "hub-logout", email: "hub-logout@example.com");
        var unaffectedUser = await SeedUserAsync(name: "hub-logout-other", email: "hub-logout-other@example.com");
        var terminatedSession = await CreateSessionAsync(terminatedUser.Id);
        var unaffectedSession = await CreateSessionAsync(unaffectedUser.Id);

        await using var terminatedConnection = CreateHubConnection(terminatedSession.Token);
        await using var unaffectedConnection = CreateHubConnection(unaffectedSession.Token);
        var terminatedNotification = ReceiveNotification(terminatedConnection);
        var unaffectedNotification = ReceiveNotification(unaffectedConnection);
        await terminatedConnection.StartAsync();
        await unaffectedConnection.StartAsync();

        Client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", terminatedSession.Token);
        (await Client.PostAsync("/api/logout", null)).StatusCode.Should().Be(HttpStatusCode.OK);

        await AssertRevokedSessionIsExcludedAsync(terminatedUser.Id, unaffectedUser.Id, terminatedNotification.Task, unaffectedNotification.Task);
    }

    [Test]
    public async Task Hub_SelfDeletion_PrunesDeletedSessionAndPreservesOtherDelivery()
    {
        var deletedUser = await SeedUserAsync(name: "hub-delete", email: "hub-delete@example.com");
        var unaffectedUser = await SeedUserAsync(name: "hub-delete-other", email: "hub-delete-other@example.com");
        var deletedSession = await CreateSessionAsync(deletedUser.Id);
        var unaffectedSession = await CreateSessionAsync(unaffectedUser.Id);

        await using var deletedConnection = CreateHubConnection(deletedSession.Token);
        await using var unaffectedConnection = CreateHubConnection(unaffectedSession.Token);
        var deletedNotification = ReceiveNotification(deletedConnection);
        var unaffectedNotification = ReceiveNotification(unaffectedConnection);
        await deletedConnection.StartAsync();
        await unaffectedConnection.StartAsync();

        Client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", deletedSession.Token);
        (await Client.GetAsync("/api/deleteAccount")).StatusCode.Should().Be(HttpStatusCode.OK);

        await AssertRevokedSessionIsExcludedAsync(deletedUser.Id, unaffectedUser.Id, deletedNotification.Task, unaffectedNotification.Task);
    }

    [Test]
    public async Task Hub_Blocking_PrunesBlockedSessionAndPreservesOtherDelivery()
    {
        var admin = await SeedAdminAsync();
        var blockedUser = await SeedUserAsync(name: "hub-block", email: "hub-block@example.com");
        var unaffectedUser = await SeedUserAsync(name: "hub-block-other", email: "hub-block-other@example.com");
        var adminSession = await CreateSessionAsync(admin.Id);
        var blockedSession = await CreateSessionAsync(blockedUser.Id);
        var unaffectedSession = await CreateSessionAsync(unaffectedUser.Id);

        await using var blockedConnection = CreateHubConnection(blockedSession.Token);
        await using var unaffectedConnection = CreateHubConnection(unaffectedSession.Token);
        var blockedNotification = ReceiveNotification(blockedConnection);
        var unaffectedNotification = ReceiveNotification(unaffectedConnection);
        await blockedConnection.StartAsync();
        await unaffectedConnection.StartAsync();

        Client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", adminSession.Token);
        (await Client.PostAsync($"/api/admin/users/{blockedUser.Id}/block", null)).StatusCode.Should().Be(HttpStatusCode.OK);

        await AssertRevokedSessionIsExcludedAsync(blockedUser.Id, unaffectedUser.Id, blockedNotification.Task, unaffectedNotification.Task);
    }

    [Test]
    public async Task Hub_MismatchedAccountAndSession_IsRejectedWhileUnaffectedSessionReceives()
    {
        var claimedUser = await SeedUserAsync(name: "hub-mismatch", email: "hub-mismatch@example.com");
        var sessionOwner = await SeedUserAsync(name: "hub-mismatch-owner", email: "hub-mismatch-owner@example.com");
        var unaffectedUser = await SeedUserAsync(name: "hub-mismatch-other", email: "hub-mismatch-other@example.com");
        var mismatchedSession = await CreateSessionAsync(sessionOwner.Id);
        var unaffectedSession = await CreateSessionAsync(unaffectedUser.Id);
        var mismatchedToken = GenerateJwt(claimedUser.Id, mismatchedSession.SessionId, mismatchedSession.Jti);

        await using var mismatchedConnection = CreateHubConnection(mismatchedToken);
        await using var unaffectedConnection = CreateHubConnection(unaffectedSession.Token);
        var unaffectedNotification = ReceiveNotification(unaffectedConnection);

        await unaffectedConnection.StartAsync();
        var start = () => mismatchedConnection.StartAsync();
        await start.Should().ThrowAsync<Exception>();

        await PublishAsync(unaffectedUser.Id, "unaffected-after-mismatch");

        var received = await unaffectedNotification.Task.WaitAsync(TimeSpan.FromSeconds(2));
        received.GetProperty("message").GetString().Should().Be("unaffected-after-mismatch");
    }

    private HubConnection CreateHubConnection(string token)
    {
        return new HubConnectionBuilder()
            .WithUrl(new Uri(Factory.Server.BaseAddress, "/hubs/notifications"), options =>
            {
                options.AccessTokenProvider = () => Task.FromResult<string?>(token);
                options.HttpMessageHandlerFactory = _ => Factory.Server.CreateHandler();
                options.Transports = HttpTransportType.LongPolling;
            })
            .Build();
    }

    private static TaskCompletionSource<JsonElement> ReceiveNotification(HubConnection connection)
    {
        var received = new TaskCompletionSource<JsonElement>(TaskCreationOptions.RunContinuationsAsynchronously);
        connection.On<JsonElement>("ReceiveNotification", notification => received.TrySetResult(notification));
        return received;
    }

    private async Task<SessionToken> CreateSessionAsync(Id<User> userId)
    {
        var sessionId = Id<UserSession>.New();
        var jti = Id<UserSession>.New().ToString();
        using (var scope = Factory.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<LgymApi.Infrastructure.Data.AppDbContext>();
            db.UserSessions.Add(new UserSession
            {
                Id = sessionId,
                UserId = userId,
                Jti = jti,
                ExpiresAtUtc = DateTimeOffset.UtcNow.AddDays(1)
            });
            await db.SaveChangesAsync();
        }

        return new SessionToken(sessionId, jti, GenerateJwt(userId, sessionId, jti));
    }

    private async Task PublishAsync(Id<User> recipientId, string message)
    {
        using var scope = Factory.Services.CreateScope();
        var publisher = scope.ServiceProvider.GetRequiredService<IInAppNotificationPushPublisher>();
        await publisher.PushAsync(new InAppNotificationResult(
            Id<InAppNotification>.New(),
            recipientId,
            message,
            "/notifications",
            false,
            InAppNotificationTypes.InvitationSent,
            true,
            null,
            DateTimeOffset.UtcNow));
    }

    private async Task AssertRevokedSessionIsExcludedAsync(
        Id<User> revokedUserId,
        Id<User> unaffectedUserId,
        Task<JsonElement> revokedNotification,
        Task<JsonElement> unaffectedNotification)
    {
        await PublishAsync(revokedUserId, "must-not-deliver");
        await AssertNotReceivedAsync(revokedNotification);

        using (var scope = Factory.Services.CreateScope())
        {
            var registry = scope.ServiceProvider.GetRequiredService<IAccountSessionConnectionRegistry>();
            registry.GetConnections(revokedUserId.Rebind<AccountReference>()).Should().BeEmpty();
        }

        await PublishAsync(unaffectedUserId, "unaffected-delivery");
        var received = await unaffectedNotification.WaitAsync(TimeSpan.FromSeconds(2));
        received.GetProperty("message").GetString().Should().Be("unaffected-delivery");
    }

    private static async Task AssertNotReceivedAsync(Task<JsonElement> notification)
    {
        var completed = await Task.WhenAny(notification, Task.Delay(TimeSpan.FromMilliseconds(500)));
        completed.Should().NotBe(notification);
    }

    private sealed record SessionToken(Id<UserSession> SessionId, string Jti, string Token);

    private sealed class NegotiateResponse
    {
        [JsonPropertyName("connectionId")]
        public string ConnectionId { get; set; } = string.Empty;

        [JsonPropertyName("availableTransports")]
        public List<AvailableTransportResponse> AvailableTransports { get; set; } = [];
    }

    private sealed class AvailableTransportResponse
    {
        [JsonPropertyName("transport")]
        public string Transport { get; set; } = string.Empty;
    }
}
