using System.Net;
using System.Net.Http.Json;
using System.Text.Json.Serialization;
using FluentAssertions;
using LgymApi.Domain.Entities;
using LgymApi.Domain.Notifications;
using LgymApi.Domain.ValueObjects;
using LgymApi.Infrastructure.Data;
using LgymApi.Resources;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace LgymApi.IntegrationTests.InAppNotifications;

[TestFixture]
public sealed class PushNotificationAdminApiTests : IntegrationTestBase
{
    [Test]
    public async Task EnqueueTestEvent_WithLinkedInAppNotification_PersistsPrivacySafePayload()
    {
        var admin = await SeedAdminAsync();
        var recipient = await SeedUserAsync(name: "push-admin-target", email: "push-admin-target@example.com");
        var linkedNotification = await SeedNotificationAsync(recipient.Id, "Sensitive test body that must stay out of push payload");
        await SeedPushInstallationAsync(recipient.Id, "device-admin-test", "token-admin-test");

        SetAuthorizationHeader(admin.Id);
        var response = await PostAsJsonWithApiOptionsAsync(
            "/api/internal/push/test-event",
            new EnqueueTestPushEventHttpRequest(
                recipient.Id.ToString(),
                "internal.test.push",
                "event-admin-test-1",
                "entity-admin-test-1",
                linkedNotification.Id.ToString(),
                "/notifications"));

        response.StatusCode.Should().Be(HttpStatusCode.OK, await response.Content.ReadAsStringAsync());
        var body = await response.Content.ReadFromJsonAsync<MessageResponse>();
        body.Should().NotBeNull();
        body!.Message.Should().Be("Push test event queued");

        using var scope = Factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var message = await db.PushNotificationMessages.SingleAsync(x => x.EventId == "event-admin-test-1");

        message.Type.Should().Be("internal.test.push");
        message.InAppNotificationId.Should().Be(linkedNotification.Id);
        message.PayloadJson.Should().Contain(linkedNotification.Id.ToString());
        message.PayloadJson.Should().Contain("event-admin-test-1");
        message.PayloadJson.Should().Contain("entity-admin-test-1");
        message.PayloadJson.Should().NotContain(linkedNotification.Message);
    }

    [Test]
    public async Task EnqueueTestEvent_NonAdminUser_ReturnsForbidden()
    {
        var user = await SeedUserAsync(name: "push-admin-forbidden", email: "push-admin-forbidden@example.com");
        var recipient = await SeedUserAsync(name: "push-admin-forbidden-target", email: "push-admin-forbidden-target@example.com");
        await SeedPushInstallationAsync(recipient.Id, "device-admin-forbidden", "token-admin-forbidden");

        SetAuthorizationHeader(user.Id);
        var response = await PostAsJsonWithApiOptionsAsync(
            "/api/internal/push/test-event",
            new EnqueueTestPushEventHttpRequest(
                recipient.Id.ToString(),
                "internal.test.push",
                "event-admin-test-2",
                null,
                null,
                null));

        response.StatusCode.Should().Be(HttpStatusCode.Forbidden);
        var body = await response.Content.ReadFromJsonAsync<MessageResponse>();
        body.Should().NotBeNull();
        body!.Message.Should().Be(CompatibilityResourceMessage.InCulture("en", () => Messages.Unauthorized));

        using var scope = Factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var count = await db.PushNotificationMessages.CountAsync(x => x.EventId == "event-admin-test-2");
        count.Should().Be(0);
    }

    [Test]
    public async Task EnqueueTestEvent_WithoutAuthorization_ReturnsUnauthorized()
    {
        var response = await PostAsJsonWithApiOptionsAsync(
            "/api/internal/push/test-event",
            new EnqueueTestPushEventHttpRequest(
                Id<User>.New().ToString(),
                "internal.test.push",
                "event-admin-test-anonymous",
                null,
                null,
                null));

        response.StatusCode.Should().Be(HttpStatusCode.Unauthorized);
        var body = await response.Content.ReadFromJsonAsync<MessageResponse>();
        body.Should().NotBeNull();
        body!.Message.Should().Be(CompatibilityResourceMessage.InCulture("en", () => Messages.InvalidToken));

        using var scope = Factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        (await db.PushNotificationMessages.CountAsync(x => x.EventId == "event-admin-test-anonymous")).Should().Be(0);
    }

    private async Task<InAppNotification> SeedNotificationAsync(Id<User> recipientId, string message)
    {
        using var scope = Factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();

        var notification = new InAppNotification
        {
            Id = Id<InAppNotification>.New(),
            RecipientId = recipientId,
            IsSystemNotification = true,
            Message = message,
            RedirectUrl = "/app/notifications",
            IsRead = false,
            Type = InAppNotificationTypes.InvitationSent,
            CreatedAt = DateTimeOffset.UtcNow,
            UpdatedAt = DateTimeOffset.UtcNow
        };

        db.InAppNotifications.Add(notification);
        await db.SaveChangesAsync();
        return notification;
    }

    private async Task SeedPushInstallationAsync(Id<User> userId, string installationId, string token)
    {
        using var scope = Factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();

        db.PushInstallations.Add(new PushInstallation
        {
            Id = Id<PushInstallation>.New(),
            UserId = userId,
            InstallationId = installationId,
            Platform = "android",
            FcmToken = token,
            Environment = "development",
            PermissionStatus = "authorized",
            LastSeenAt = DateTimeOffset.UtcNow
        });

        await db.SaveChangesAsync();
    }

    private sealed record EnqueueTestPushEventHttpRequest(
        [property: JsonPropertyName("recipientUserId")] string RecipientUserId,
        [property: JsonPropertyName("type")] string Type,
        [property: JsonPropertyName("eventId")] string EventId,
        [property: JsonPropertyName("entityId")] string? EntityId,
        [property: JsonPropertyName("inAppNotificationId")] string? InAppNotificationId,
        [property: JsonPropertyName("deeplink")] string? Deeplink);

    private sealed class MessageResponse
    {
        [JsonPropertyName("msg")]
        public string Message { get; set; } = string.Empty;
    }
}
