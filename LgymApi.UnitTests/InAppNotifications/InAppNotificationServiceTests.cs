using FluentAssertions;
using LgymApi.Application.BuildingBlocks.Errors;
using LgymApi.Application.BuildingBlocks.Results;
using LgymApi.Application.Repositories;
using LgymApi.Domain.Entities;
using LgymApi.Domain.ValueObjects;
using InAppNotificationService = global::LgymApi.Application.Notifications.InAppNotificationService;
using IInAppNotificationRepository = global::LgymApi.Application.Notifications.IInAppNotificationRepository;
using IInAppNotificationPushPublisher = global::LgymApi.Application.Notifications.IInAppNotificationPushPublisher;
using INotificationEventBridge = global::LgymApi.Application.Notifications.INotificationEventBridge;
using LgymApi.Application.Notifications.Errors;
using LgymApi.Application.Notifications.Models;
using LgymApi.Domain.Notifications;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using NUnit.Framework;

namespace LgymApi.UnitTests.InAppNotifications;

[TestFixture]
public sealed class InAppNotificationServiceTests
{
    [Test]
    public void InAppNotificationService_UsesTheFourDirectDeliveryCollaborators()
    {
        var constructor = typeof(InAppNotificationService).GetConstructors().Should().ContainSingle().Which;

        constructor.GetParameters().Select(parameter => parameter.ParameterType).Should().Equal(
            typeof(IInAppNotificationRepository),
            typeof(IUnitOfWork),
            typeof(IInAppNotificationPushPublisher),
            typeof(INotificationEventBridge),
            typeof(Microsoft.Extensions.Logging.ILogger<InAppNotificationService>));
    }

    [Test]
    public async Task CreateAsync_ValidInput_ReturnsSuccess()
    {
        var repo = new FakeInAppNotificationRepository();
        var service = CreateService(repository: repo);
        var userId = Id<User>.New();
        var input = new CreateInAppNotificationInput(
            userId, null, null, true, "Test message", null, InAppNotificationTypes.InvitationSent);

        var result = await service.CreateAsync(input);

        result.IsSuccess.Should().BeTrue();
        result.Value.Message.Should().Be("Test message");
        result.Value.Type.Should().Be(InAppNotificationTypes.InvitationSent);
    }

    [Test]
    public async Task CreateAsync_ValidInput_PersistsNotification()
    {
        var repo = new FakeInAppNotificationRepository();
        var service = CreateService(repository: repo);
        var userId = Id<User>.New();
        var input = new CreateInAppNotificationInput(
            userId, null, null, true, "Persisted", null, InAppNotificationTypes.InvitationSent);

        await service.CreateAsync(input);

        repo.Added.Should().HaveCount(1);
        repo.Added[0].Message.Should().Be("Persisted");
        repo.Added[0].RecipientId.Should().Be(userId);
        repo.Added[0].IsRead.Should().BeFalse();
    }

    [Test]
    public async Task CreateAsync_ValidInput_PushesNotification()
    {
        var push = new FakePushPublisher();
        var service = CreateService(pushPublisher: push);
        var input = new CreateInAppNotificationInput(
            Id<User>.New(), null, null, true, "Push me", null, InAppNotificationTypes.InvitationSent);

        await service.CreateAsync(input);

        push.PushCalls.Should().Be(1);
        push.LastPushed!.Message.Should().Be("Push me");
    }

    [Test]
    public async Task CreateAsync_ValidInput_EnqueuesRemotePushNotification()
    {
        var bridge = new FakeNotificationEventBridge();
        var service = CreateService(notificationEventBridge: bridge);
        var userId = Id<User>.New();
        var input = new CreateInAppNotificationInput(
            userId,
            null,
            null,
            true,
            "Report request received",
            "/trainer/report-requests/request-1",
            InAppNotificationTypes.ReportRequestReceived);

        var result = await service.CreateAsync(input);

        bridge.EnqueueCalls.Should().Be(1);
        bridge.LastInput.Should().NotBeNull();
        bridge.LastInput!.UserId.Should().Be(userId);
        bridge.LastInput.SchemaVersion.Should().Be(1);
        bridge.LastInput.Type.Should().Be(InAppNotificationTypes.ReportRequestReceived.Value);
        bridge.LastInput.EventKey.Should().Be(result.Value.Id.ToString());
        bridge.LastInput.EntityKey.Should().BeNull();
        bridge.LastInput.InAppNotificationId.Should().Be(result.Value.Id);
        bridge.LastInput.Deeplink.Should().Be("/trainer/report-requests/request-1");
    }

    [Test]
    public async Task CreateAsync_RemotePushEnqueueThrows_PropagatesForRetry()
    {
        var bridge = new FakeNotificationEventBridge { ThrowOnEnqueue = true };
        var service = CreateService(notificationEventBridge: bridge);
        var input = new CreateInAppNotificationInput(
            Id<User>.New(), null, null, true, "Still ok", null, InAppNotificationTypes.InvitationSent);

        var action = async () => await service.CreateAsync(input);

        await action.Should().ThrowAsync<InvalidOperationException>()
            .WithMessage("Remote push enqueue failed");
        bridge.EnqueueCalls.Should().Be(1);
    }

    [Test]
    public async Task CreateAsync_PushThrows_ReturnsSuccess()
    {
        var push = new FakePushPublisher { ThrowOnPush = true };
        var service = CreateService(pushPublisher: push);
        var input = new CreateInAppNotificationInput(
            Id<User>.New(), null, null, true, "Still ok", null, InAppNotificationTypes.InvitationSent);

        var result = await service.CreateAsync(input);

        result.IsSuccess.Should().BeTrue();
        result.Value.Message.Should().Be("Still ok");
    }

    [Test]
    public async Task GetForUserAsync_NoNotifications_ReturnsEmptyPage()
    {
        var service = CreateService();
        var userId = Id<User>.New();
        var query = new CursorPaginationQuery();

        var result = await service.GetForUserAsync(userId, query);

        result.IsSuccess.Should().BeTrue();
        result.Value.Items.Should().BeEmpty();
        result.Value.HasNextPage.Should().BeFalse();
    }

    [Test]
    public async Task GetForUserAsync_WithNotifications_ReturnsPaged()
    {
        var repo = new FakeInAppNotificationRepository();
        var userId = Id<User>.New();
        AddNotification(repo, userId, "First");
        AddNotification(repo, userId, "Second");

        var service = CreateService(repository: repo);
        var query = new CursorPaginationQuery(Limit: 20);

        var result = await service.GetForUserAsync(userId, query);

        result.IsSuccess.Should().BeTrue();
        result.Value.Items.Should().HaveCount(2);
        result.Value.HasNextPage.Should().BeFalse();
    }

    [Test]
    public async Task GetForUserAsync_WithCursor_ReturnsNextPage()
    {
        var repo = new FakeInAppNotificationRepository();
        var userId = Id<User>.New();
        var now = DateTimeOffset.UtcNow;

        // Add 3 notifications with decreasing timestamps to simulate ordered results
        AddNotification(repo, userId, "N1", now.AddMinutes(-3));
        AddNotification(repo, userId, "N2", now.AddMinutes(-2));
        AddNotification(repo, userId, "N3", now.AddMinutes(-1));

        var service = CreateService(repository: repo);

        // First page with limit 2
        var query = new CursorPaginationQuery(Limit: 2);
        var result = await service.GetForUserAsync(userId, query);

        result.IsSuccess.Should().BeTrue();
        result.Value.Items.Should().HaveCount(2);
        result.Value.HasNextPage.Should().BeTrue();
        result.Value.NextCursorCreatedAt.Should().NotBeNull();
        result.Value.NextCursorId.Should().NotBeNull();
    }

    [Test]
    public async Task MarkAsReadAsync_OwnNotification_ReturnsSuccess()
    {
        var repo = new FakeInAppNotificationRepository();
        var userId = Id<User>.New();
        var notification = AddNotification(repo, userId, "Read me");

        var service = CreateService(repository: repo);

        var result = await service.MarkAsReadAsync(notification.Id, userId);

        result.IsSuccess.Should().BeTrue();
    }

    [Test]
    public async Task MarkAsReadAsync_OtherUsersNotification_ReturnsForbidden()
    {
        var repo = new FakeInAppNotificationRepository();
        var ownerId = Id<User>.New();
        var otherUserId = Id<User>.New();
        var notification = AddNotification(repo, ownerId, "Not yours");

        var service = CreateService(repository: repo);

        var result = await service.MarkAsReadAsync(notification.Id, otherUserId);

        result.IsFailure.Should().BeTrue();
        result.Error.Should().BeOfType<InAppNotificationForbiddenError>();
    }

    [Test]
    public async Task MarkAsReadAsync_NonExistentNotification_ReturnsNotFound()
    {
        var service = CreateService();
        var nonExistentId = Id<InAppNotification>.New();

        var result = await service.MarkAsReadAsync(nonExistentId, Id<User>.New());

        result.IsFailure.Should().BeTrue();
        result.Error.Should().BeOfType<InAppNotificationNotFoundError>();
    }

    [Test]
    public async Task MarkAsReadAsync_AlreadyRead_ReturnsSuccessIdempotent()
    {
        var repo = new FakeInAppNotificationRepository();
        var userId = Id<User>.New();
        var notification = AddNotification(repo, userId, "Already read");
        notification.IsRead = true;

        var service = CreateService(repository: repo);

        var result = await service.MarkAsReadAsync(notification.Id, userId);

        result.IsSuccess.Should().BeTrue();
    }

    [Test]
    public async Task MarkAllAsReadAsync_MarksAllUnread()
    {
        var repo = new FakeInAppNotificationRepository();
        var userId = Id<User>.New();
        AddNotification(repo, userId, "Unread 1");
        AddNotification(repo, userId, "Unread 2");

        var service = CreateService(repository: repo);

        var result = await service.MarkAllAsReadAsync(userId, before: null);

        result.IsSuccess.Should().BeTrue();
        repo.MarkAllAsReadCalls.Should().Be(1);
        repo.LastMarkAllAsReadUserId.Should().Be(userId);
        repo.LastMarkAllAsReadBefore.Should().BeNull();
    }

    [Test]
    public async Task MarkAllAsReadAsync_WithBefore_MarksBefore()
    {
        var repo = new FakeInAppNotificationRepository();
        var userId = Id<User>.New();
        var before = DateTimeOffset.UtcNow;

        var service = CreateService(repository: repo);

        var result = await service.MarkAllAsReadAsync(userId, before);

        result.IsSuccess.Should().BeTrue();
        repo.MarkAllAsReadCalls.Should().Be(1);
        repo.LastMarkAllAsReadBefore.Should().Be(before);
    }

    [Test]
    public async Task GetUnreadCountAsync_ReturnsCorrectCount()
    {
        var repo = new FakeInAppNotificationRepository();
        var userId = Id<User>.New();
        AddNotification(repo, userId, "Unread 1");
        AddNotification(repo, userId, "Unread 2");
        var readNotification = AddNotification(repo, userId, "Read");
        readNotification.IsRead = true;

        var service = CreateService(repository: repo);

        var result = await service.GetUnreadCountAsync(userId);

        result.IsSuccess.Should().BeTrue();
        result.Value.Should().Be(2);
    }

    [Test]
    public async Task CreateAsync_DuplicateDeliveryKey_ReturnsExistingNotificationWithoutSignalRPushAndEnqueuesRemotePush()
    {
        var repo = new FakeInAppNotificationRepository();
        var unitOfWork = new FakeUnitOfWork
        {
            SaveChangesException = new DbUpdateException("duplicate IX_in_app_notifications_RecipientId_Type_DeliveryKey")
        };
        var push = new FakePushPublisher();
        var bridge = new FakeNotificationEventBridge();
        var userId = Id<User>.New();
        var existing = AddNotification(repo, userId, "Already there");
        existing.Type = InAppNotificationTypes.InvitationSent;
        existing.DeliveryKey = "trainer-invitation:abc:sent";

        var service = CreateService(repository: repo, unitOfWork: unitOfWork, pushPublisher: push, notificationEventBridge: bridge);
        var input = new CreateInAppNotificationInput(
            userId,
            null,
            "trainer-invitation:abc:sent",
            true,
            "Already there",
            null,
            InAppNotificationTypes.InvitationSent);

        var result = await service.CreateAsync(input);

        result.IsSuccess.Should().BeTrue();
        result.Value.Id.Should().Be(existing.Id);
        push.PushCalls.Should().Be(0);
        bridge.EnqueueCalls.Should().Be(1);
        bridge.LastInput!.InAppNotificationId.Should().Be(existing.Id);
        bridge.LastInput.EventKey.Should().Be(existing.Id.ToString());
        unitOfWork.SaveChangesCalls.Should().Be(1);
        repo.DetachCalls.Should().Be(1);
    }

    #region Helpers

    private static InAppNotificationService CreateService(
        FakeInAppNotificationRepository? repository = null,
        FakeUnitOfWork? unitOfWork = null,
        FakePushPublisher? pushPublisher = null,
        FakeNotificationEventBridge? notificationEventBridge = null)
    {
        return new InAppNotificationService(
            repository ?? new FakeInAppNotificationRepository(),
            unitOfWork ?? new FakeUnitOfWork(),
            pushPublisher ?? new FakePushPublisher(),
            notificationEventBridge ?? new FakeNotificationEventBridge(),
            NullLogger<InAppNotificationService>.Instance);
    }

    private static InAppNotification AddNotification(
        FakeInAppNotificationRepository repo,
        Id<User> userId,
        string message,
        DateTimeOffset? createdAt = null)
    {
        var notification = new InAppNotification
        {
            Id = Id<InAppNotification>.New(),
            RecipientId = userId,
            SenderUserId = null,
            DeliveryKey = null,
            IsSystemNotification = true,
            Message = message,
            RedirectUrl = null,
            IsRead = false,
            Type = InAppNotificationTypes.InvitationSent,
            CreatedAt = createdAt ?? DateTimeOffset.UtcNow,
        };
        repo.Added.Add(notification);
        return notification;
    }

    #endregion

    #region Fakes

    internal sealed class FakeInAppNotificationRepository : IInAppNotificationRepository
    {
        public List<InAppNotification> Added { get; } = new();
        public int DetachCalls { get; private set; }
        public int MarkAsReadCalls { get; private set; }
        public int MarkAllAsReadCalls { get; private set; }
        public Id<User> LastMarkAllAsReadUserId { get; private set; }
        public DateTimeOffset? LastMarkAllAsReadBefore { get; private set; }

        public Task AddAsync(InAppNotification notification, CancellationToken cancellationToken = default)
        {
            Added.Add(notification);
            return Task.CompletedTask;
        }

        public Task<InAppNotification?> FindByDeliveryKeyAsync(
            Id<User> recipientId,
            InAppNotificationType type,
            string deliveryKey,
            CancellationToken cancellationToken = default)
        {
            var found = Added.FirstOrDefault(n => n.RecipientId == recipientId && n.Type == type && n.DeliveryKey == deliveryKey);
            return Task.FromResult<InAppNotification?>(found);
        }

        public void Detach(InAppNotification notification)
        {
            DetachCalls++;
            Added.Remove(notification);
        }

        public Task<InAppNotification?> GetByIdAsync(Id<InAppNotification> id, CancellationToken cancellationToken = default)
        {
            var found = Added.FirstOrDefault(n => n.Id == id);
            return Task.FromResult<InAppNotification?>(found);
        }

        public Task<IReadOnlyList<InAppNotification>> GetPageAsync(
            Id<User> userId,
            int limit,
            DateTimeOffset? cursorCreatedAt,
            Id<InAppNotification>? cursorId,
            CancellationToken cancellationToken = default)
        {
            var query = Added.Where(n => n.RecipientId == userId);

            if (cursorCreatedAt.HasValue && cursorId.HasValue)
            {
                query = query.Where(n =>
                    n.CreatedAt < cursorCreatedAt.Value ||
                    (n.CreatedAt == cursorCreatedAt.Value && n.Id.GetValue().CompareTo(cursorId.Value.GetValue()) < 0));
            }

            var result = query
                .OrderByDescending(n => n.CreatedAt)
                .ThenByDescending(n => n.Id.GetValue())
                .Take(limit)
                .ToList();

            return Task.FromResult<IReadOnlyList<InAppNotification>>(result);
        }

        public Task MarkAsReadAsync(Id<InAppNotification> id, CancellationToken cancellationToken = default)
        {
            MarkAsReadCalls++;
            var notification = Added.FirstOrDefault(n => n.Id == id);
            if (notification != null)
            {
                notification.IsRead = true;
            }
            return Task.CompletedTask;
        }

        public Task MarkAllAsReadAsync(Id<User> userId, DateTimeOffset? before, CancellationToken cancellationToken = default)
        {
            MarkAllAsReadCalls++;
            LastMarkAllAsReadUserId = userId;
            LastMarkAllAsReadBefore = before;

            var toMark = Added.Where(n => n.RecipientId == userId && !n.IsRead);
            if (before.HasValue)
            {
                toMark = toMark.Where(n => n.CreatedAt < before.Value);
            }
            foreach (var n in toMark.ToList())
            {
                n.IsRead = true;
            }
            return Task.CompletedTask;
        }

        public Task<int> GetUnreadCountAsync(Id<User> userId, CancellationToken cancellationToken = default)
        {
            var count = Added.Count(n => n.RecipientId == userId && !n.IsRead);
            return Task.FromResult(count);
        }
    }

    private sealed class FakeUnitOfWork : IUnitOfWork
    {
        public int SaveChangesCalls { get; private set; }
        public Exception? SaveChangesException { get; set; }

        public Task<int> SaveChangesAsync(CancellationToken cancellationToken = default)
        {
            SaveChangesCalls++;
            if (SaveChangesException != null)
            {
                throw SaveChangesException;
            }

            return Task.FromResult(1);
        }

        public Task<IUnitOfWorkTransaction> BeginTransactionAsync(CancellationToken cancellationToken = default)
        {
            return Task.FromResult<IUnitOfWorkTransaction>(new FakeTransaction());
        }
    }

    private sealed class FakeTransaction : IUnitOfWorkTransaction
    {
        public ValueTask DisposeAsync() => ValueTask.CompletedTask;
        public Task CommitAsync(CancellationToken cancellationToken = default) => Task.CompletedTask;
        public Task RollbackAsync(CancellationToken cancellationToken = default) => Task.CompletedTask;
    }

    private sealed class FakePushPublisher : IInAppNotificationPushPublisher
    {
        public int PushCalls { get; private set; }
        public InAppNotificationResult? LastPushed { get; private set; }
        public bool ThrowOnPush { get; set; }

        public Task PushAsync(InAppNotificationResult notification, CancellationToken ct = default)
        {
            PushCalls++;
            LastPushed = notification;
            if (ThrowOnPush)
            {
                throw new InvalidOperationException("Push failed");
            }
            return Task.CompletedTask;
        }
    }

    private sealed class FakeNotificationEventBridge : INotificationEventBridge
    {
        public int EnqueueCalls { get; private set; }
        public EnqueueNotificationEventInput? LastInput { get; private set; }
        public bool ThrowOnEnqueue { get; set; }

        public Task EnqueueAsync(EnqueueNotificationEventInput input, CancellationToken cancellationToken = default)
        {
            EnqueueCalls++;
            LastInput = input;
            if (ThrowOnEnqueue)
            {
                throw new InvalidOperationException("Remote push enqueue failed");
            }

            return Task.CompletedTask;
        }
    }

    #endregion
}
