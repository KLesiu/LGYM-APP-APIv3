using LgymApi.Application.BuildingBlocks.Errors;
using LgymApi.Application.BuildingBlocks.Results;
using LgymApi.Application.Notifications;
using LgymApi.Application.Notifications.Models;
using LgymApi.Domain.ValueObjects;
using LgymApi.Identity.Contracts;
using InAppNotificationEntity = LgymApi.Domain.Entities.InAppNotification;
using UserEntity = LgymApi.Domain.Entities.User;

namespace LgymApi.Application.Task7ApiCompatibility;

public interface IInAppNotificationApiCompatibilityAdapter
{
    Task<Result<PagedResult<InAppNotificationResult>, AppError>> GetForAccountAsync(
        Id<AccountReference> accountId,
        CursorPaginationQuery query,
        CancellationToken cancellationToken = default);

    Task<Result<Unit, AppError>> MarkAsReadAsync(
        Id<InAppNotificationEntity> notificationId,
        Id<AccountReference> requestingAccountId,
        CancellationToken cancellationToken = default);

    Task<Result<Unit, AppError>> MarkAllAsReadAsync(
        Id<AccountReference> accountId,
        DateTimeOffset? before,
        CancellationToken cancellationToken = default);

    Task<Result<int, AppError>> GetUnreadCountAsync(
        Id<AccountReference> accountId,
        CancellationToken cancellationToken = default);
}

public sealed record EnqueueAccountNotificationEventInput(
    Id<AccountReference> RecipientAccountId,
    int SchemaVersion,
    string Type,
    string EventKey,
    string? EntityKey,
    Id<InAppNotificationEntity>? InAppNotificationId,
    string? Deeplink);

public interface INotificationEventApiCompatibilityAdapter
{
    Task EnqueueAsync(
        EnqueueAccountNotificationEventInput input,
        CancellationToken cancellationToken = default);
}

internal sealed class InAppNotificationApiCompatibilityAdapter : IInAppNotificationApiCompatibilityAdapter
{
    private readonly IInAppNotificationService _notificationService;

    public InAppNotificationApiCompatibilityAdapter(IInAppNotificationService notificationService)
    {
        _notificationService = notificationService;
    }

    public Task<Result<PagedResult<InAppNotificationResult>, AppError>> GetForAccountAsync(
        Id<AccountReference> accountId,
        CursorPaginationQuery query,
        CancellationToken cancellationToken = default)
        => _notificationService.GetForUserAsync(accountId.Rebind<UserEntity>(), query, cancellationToken);

    public Task<Result<Unit, AppError>> MarkAsReadAsync(
        Id<InAppNotificationEntity> notificationId,
        Id<AccountReference> requestingAccountId,
        CancellationToken cancellationToken = default)
        => _notificationService.MarkAsReadAsync(notificationId, requestingAccountId.Rebind<UserEntity>(), cancellationToken);

    public Task<Result<Unit, AppError>> MarkAllAsReadAsync(
        Id<AccountReference> accountId,
        DateTimeOffset? before,
        CancellationToken cancellationToken = default)
        => _notificationService.MarkAllAsReadAsync(accountId.Rebind<UserEntity>(), before, cancellationToken);

    public Task<Result<int, AppError>> GetUnreadCountAsync(
        Id<AccountReference> accountId,
        CancellationToken cancellationToken = default)
        => _notificationService.GetUnreadCountAsync(accountId.Rebind<UserEntity>(), cancellationToken);
}

internal sealed class NotificationEventApiCompatibilityAdapter : INotificationEventApiCompatibilityAdapter
{
    private readonly INotificationEventBridge _notificationEventBridge;

    public NotificationEventApiCompatibilityAdapter(INotificationEventBridge notificationEventBridge)
    {
        _notificationEventBridge = notificationEventBridge;
    }

    public Task EnqueueAsync(
        EnqueueAccountNotificationEventInput input,
        CancellationToken cancellationToken = default)
        => _notificationEventBridge.EnqueueAsync(
            new EnqueueNotificationEventInput(
                input.RecipientAccountId.Rebind<UserEntity>(),
                input.SchemaVersion,
                input.Type,
                input.EventKey,
                input.EntityKey,
                input.InAppNotificationId,
                input.Deeplink),
            cancellationToken);
}
