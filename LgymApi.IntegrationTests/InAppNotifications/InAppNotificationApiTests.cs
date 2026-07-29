using System.Net;
using System.Net.Http.Json;
using System.Text.Json.Serialization;
using FluentAssertions;
using LgymApi.Domain.Entities;
using LgymApi.Domain.Notifications;
using LgymApi.Domain.ValueObjects;
using LgymApi.Infrastructure.Data;
using NotificationEntity = global::LgymApi.Domain.Entities.InAppNotification;
using NotificationTypes = global::LgymApi.Domain.Notifications.InAppNotificationTypes;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace LgymApi.IntegrationTests.InAppNotifications;

[TestFixture]
public sealed class InAppNotificationApiTests : IntegrationTestBase
{
    [Test]
    public async Task GetNotifications_NewUser_ReturnsEmptyPage()
    {
        var user = await SeedUserAsync(name: "notif-empty", email: "notif-empty@example.com");
        SetAuthorizationHeader(user.Id);

        var response = await Client.GetAsync($"/api/{user.Id}/notifications");

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        var body = await response.Content.ReadFromJsonAsync<PagedNotificationsResponse>();
        body.Should().NotBeNull();
        body!.Items.Should().BeEmpty();
        body.HasNextPage.Should().BeFalse();
        body.NextCursorCreatedAt.Should().BeNull();
        body.NextCursorId.Should().BeNull();
    }

    [TestCase(0)]
    [TestCase(51)]
    public async Task GetNotifications_LimitOutOfRange_Returns400(int limit)
    {
        var user = await SeedUserAsync(name: $"notif-limit-{limit}", email: $"notif-limit-{limit}@example.com");
        SetAuthorizationHeader(user.Id);

        var response = await Client.GetAsync($"/api/{user.Id}/notifications?limit={limit}");

        response.StatusCode.Should().Be(HttpStatusCode.BadRequest);
    }

    [Test]
    public async Task GetNotifications_InvalidCursorId_PreservesTheInitialPageResponse()
    {
        var user = await SeedUserAsync(name: "notif-invalid-cursor", email: "notif-invalid-cursor@example.com");
        var notification = await SeedNotificationAsync(user.Id, "initial-page-notification");
        SetAuthorizationHeader(user.Id);

        var cursorCreatedAt = Uri.EscapeDataString(notification.CreatedAt.ToString("O"));
        var response = await Client.GetAsync($"/api/{user.Id}/notifications?cursorCreatedAt={cursorCreatedAt}&cursorId=not-a-notification-id");

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        var body = await response.Content.ReadFromJsonAsync<PagedNotificationsResponse>();
        body.Should().NotBeNull();
        body!.Items.Select(item => item.Id).Should().Contain(notification.Id.ToString());
    }

    [Test]
    public async Task GetNotifications_WithSeededData_ReturnsPaged()
    {
        var user = await SeedUserAsync(name: "notif-page", email: "notif-page@example.com");
        var sender = await SeedUserAsync(name: "notif-page-sender", email: "notif-page-sender@example.com");

        var oldest = await SeedNotificationAsync(user.Id, "first", createdAt: new DateTimeOffset(2026, 4, 4, 8, 0, 0, TimeSpan.Zero));
        var middle = await SeedNotificationAsync(user.Id, "second", senderUserId: sender.Id, createdAt: new DateTimeOffset(2026, 4, 4, 9, 0, 0, TimeSpan.Zero));
        var newest = await SeedNotificationAsync(user.Id, "third", isRead: true, createdAt: new DateTimeOffset(2026, 4, 4, 10, 0, 0, TimeSpan.Zero));

        SetAuthorizationHeader(user.Id);
        var response = await Client.GetAsync($"/api/{user.Id}/notifications?limit=2");

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        var body = await response.Content.ReadFromJsonAsync<PagedNotificationsResponse>();
        body.Should().NotBeNull();
        body!.Items.Should().HaveCount(2);
        body.Items.Select(x => x.Id).Should().ContainInOrder(newest.Id.ToString(), middle.Id.ToString());
        body.Items.Select(x => x.Message).Should().ContainInOrder("third", "second");
        body.Items[0].IsRead.Should().BeTrue();
        body.Items[1].SenderUserId.Should().Be(sender.Id.ToString());
        body.HasNextPage.Should().BeTrue();
        body.NextCursorCreatedAt.Should().Be(middle.CreatedAt);
        body.NextCursorId.Should().Be(middle.Id.ToString());
        body.Items.Select(x => x.Id).Should().NotContain(oldest.Id.ToString());
    }

    [Test]
    public async Task MarkAsRead_NotFound_Returns404()
    {
        var user = await SeedUserAsync(name: "notif-missing", email: "notif-missing@example.com");
        SetAuthorizationHeader(user.Id);

        var response = await Client.PostAsync($"/api/{user.Id}/notifications/{Id<NotificationEntity>.New()}/mark-read", null);

        response.StatusCode.Should().Be(HttpStatusCode.NotFound);
    }

    [Test]
    public async Task MarkAsRead_OtherUser_Returns403()
    {
        var owner = await SeedUserAsync(name: "notif-owner", email: "notif-owner@example.com");
        var otherUser = await SeedUserAsync(name: "notif-other", email: "notif-other@example.com");
        var notification = await SeedNotificationAsync(owner.Id, "owner-only");

        SetAuthorizationHeader(otherUser.Id);
        var response = await Client.PostAsync($"/api/{otherUser.Id}/notifications/{notification.Id}/mark-read", null);

        response.StatusCode.Should().Be(HttpStatusCode.Forbidden);
        var body = await response.Content.ReadFromJsonAsync<MessageResponse>();
        body.Should().NotBeNull();
        body!.Message.Should().Be("Notification forbidden");

        using var scope = Factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var stored = await db.InAppNotifications.SingleAsync(x => x.Id == notification.Id);
        stored.IsRead.Should().BeFalse();
    }

    [Test]
    public async Task MarkAsRead_OwnNotification_Returns200()
    {
        var user = await SeedUserAsync(name: "notif-read", email: "notif-read@example.com");
        var notification = await SeedNotificationAsync(user.Id, "mark-me");
        SetAuthorizationHeader(user.Id);

        var response = await Client.PostAsync($"/api/{user.Id}/notifications/{notification.Id}/mark-read", null);

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        var body = await response.Content.ReadFromJsonAsync<MessageResponse>();
        body.Should().NotBeNull();
        body!.Message.Should().NotBeNullOrWhiteSpace();

        using var scope = Factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var stored = await db.InAppNotifications.SingleAsync(x => x.Id == notification.Id);
        stored.IsRead.Should().BeTrue();
    }

    [Test]
    public async Task MarkAllAsRead_Returns200()
    {
        var user = await SeedUserAsync(name: "notif-read-all", email: "notif-read-all@example.com");
        await SeedNotificationAsync(user.Id, "one");
        await SeedNotificationAsync(user.Id, "two");
        await SeedNotificationAsync(user.Id, "already-read", isRead: true);

        SetAuthorizationHeader(user.Id);
        var response = await Client.PostAsync($"/api/{user.Id}/notifications/mark-all-read", null);

        response.StatusCode.Should().Be(HttpStatusCode.OK);

        using var scope = Factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var unreadCount = await db.InAppNotifications.CountAsync(x => x.RecipientId == user.Id && !x.IsRead);
        unreadCount.Should().Be(0);
    }

    [Test]
    public async Task GetUnreadCount_ReturnsCorrectCount()
    {
        var user = await SeedUserAsync(name: "notif-count", email: "notif-count@example.com");
        var otherUser = await SeedUserAsync(name: "notif-count-other", email: "notif-count-other@example.com");

        await SeedNotificationAsync(user.Id, "unread-1");
        await SeedNotificationAsync(user.Id, "unread-2");
        await SeedNotificationAsync(user.Id, "read", isRead: true);
        await SeedNotificationAsync(user.Id, "deleted", isDeleted: true);
        await SeedNotificationAsync(otherUser.Id, "other-user");

        SetAuthorizationHeader(user.Id);
        var response = await Client.GetAsync($"/api/{user.Id}/notifications/unread-count");

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        var body = await response.Content.ReadFromJsonAsync<UnreadCountResponse>();
        body.Should().NotBeNull();
        body!.Count.Should().Be(2);
    }

    private async Task<NotificationEntity> SeedNotificationAsync(
        Id<User> recipientId,
        string message,
        Id<User>? senderUserId = null,
        bool isRead = false,
        bool isDeleted = false,
        DateTimeOffset? createdAt = null)
    {
        using var scope = Factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();

        var timestamp = createdAt ?? DateTimeOffset.UtcNow;
        var notification = new NotificationEntity
        {
            Id = Id<NotificationEntity>.New(),
            RecipientId = recipientId,
            SenderUserId = senderUserId,
            IsSystemNotification = senderUserId is null,
            Message = message,
            RedirectUrl = "/app/notifications",
            IsRead = isRead,
            Type = NotificationTypes.InvitationSent,
            CreatedAt = timestamp,
            UpdatedAt = timestamp,
            IsDeleted = isDeleted
        };

        db.InAppNotifications.Add(notification);
        await db.SaveChangesAsync();
        return notification;
    }

    private sealed class MessageResponse
    {
        [JsonPropertyName("msg")]
        public string Message { get; set; } = string.Empty;
    }

    private sealed class PagedNotificationsResponse
    {
        [JsonPropertyName("items")]
        public List<InAppNotificationResponse> Items { get; set; } = [];

        [JsonPropertyName("hasNextPage")]
        public bool HasNextPage { get; set; }

        [JsonPropertyName("nextCursorCreatedAt")]
        public DateTimeOffset? NextCursorCreatedAt { get; set; }

        [JsonPropertyName("nextCursorId")]
        public string? NextCursorId { get; set; }
    }

    private sealed class InAppNotificationResponse
    {
        [JsonPropertyName("_id")]
        public string Id { get; set; } = string.Empty;

        [JsonPropertyName("message")]
        public string Message { get; set; } = string.Empty;

        [JsonPropertyName("senderUserId")]
        public string? SenderUserId { get; set; }

        [JsonPropertyName("isRead")]
        public bool IsRead { get; set; }
    }

    private sealed class UnreadCountResponse
    {
        [JsonPropertyName("count")]
        public int Count { get; set; }
    }
}
