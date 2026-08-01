using LgymApi.Application.BuildingBlocks.Errors;
using LgymApi.Application.BuildingBlocks.Results;
using LgymApi.Application.Notifications.Errors;
using LgymApi.Application.Notifications.Models;
using LgymApi.Application.Repositories;
using LgymApi.Domain.Entities;
using LgymApi.Domain.Notifications;
using LgymApi.Domain.ValueObjects;
using Microsoft.Extensions.Logging;

namespace LgymApi.Application.Notifications;

internal sealed class InAppNotificationService : IInAppNotificationService
{
    private const int PushSchemaVersion = 1;

    private readonly IInAppNotificationRepository _inAppNotificationRepository;
    private readonly IUnitOfWork _unitOfWork;
    private readonly IInAppNotificationPushPublisher _pushPublisher;
    private readonly INotificationEventBridge _notificationEventBridge;
    private readonly ILogger<InAppNotificationService> _logger;

    public InAppNotificationService(
        IInAppNotificationRepository inAppNotificationRepository,
        IUnitOfWork unitOfWork,
        IInAppNotificationPushPublisher pushPublisher,
        INotificationEventBridge notificationEventBridge,
        ILogger<InAppNotificationService> logger)
    {
        _inAppNotificationRepository = inAppNotificationRepository;
        _unitOfWork = unitOfWork;
        _pushPublisher = pushPublisher;
        _notificationEventBridge = notificationEventBridge;
        _logger = logger;
    }

    public async Task<Result<InAppNotificationResult, AppError>> CreateAsync(CreateInAppNotificationInput input, CancellationToken cancellationToken = default)
    {
        var isNewNotification = true;
        var notification = new InAppNotification
        {
            Id = Id<InAppNotification>.New(),
            RecipientId = input.RecipientId,
            SenderUserId = input.SenderUserId,
            DeliveryKey = input.DeliveryKey,
            IsSystemNotification = input.IsSystemNotification,
            Message = input.Message,
            RedirectUrl = input.RedirectUrl,
            IsRead = false,
            Type = input.Type,
        };

        await _inAppNotificationRepository.AddAsync(notification, cancellationToken);

        InAppNotificationResult result;
        try
        {
            await _unitOfWork.SaveChangesAsync(cancellationToken);
            result = MapToResult(notification);
        }
        catch (Exception ex)
        {
            var existingResult = await TryResolveDuplicateDeliveryAsync(input, notification, ex, cancellationToken);
            if (existingResult != null)
            {
                result = existingResult;
                isNewNotification = false;
            }
            else
            {
                throw;
            }
        }

        if (isNewNotification)
        {
            try
            {
                await _pushPublisher.PushAsync(result, cancellationToken);
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Failed to push notification for recipient {RecipientId}", input.RecipientId);
            }
        }

        await _notificationEventBridge.EnqueueAsync(
            new EnqueueNotificationEventInput(
                result.RecipientId,
                PushSchemaVersion,
                result.Type.Value,
                result.Id.ToString(),
                null,
                result.Id,
                result.RedirectUrl),
            cancellationToken);

        return Result<InAppNotificationResult, AppError>.Success(result);
    }

    public async Task<Result<PagedResult<InAppNotificationResult>, AppError>> GetForUserAsync(Id<User> userId, CursorPaginationQuery query, CancellationToken cancellationToken = default)
    {
        var items = await _inAppNotificationRepository.GetPageAsync(userId, query.Limit + 1, query.CursorCreatedAt, query.CursorId, cancellationToken);

        var hasNextPage = items.Count > query.Limit;
        if (hasNextPage)
        {
            items = items.Take(query.Limit).ToList();
        }

        var lastItem = items.LastOrDefault();
        var resultItems = items.Select(MapToResult).ToList();

        return Result<PagedResult<InAppNotificationResult>, AppError>.Success(new PagedResult<InAppNotificationResult>(resultItems, hasNextPage, hasNextPage ? lastItem?.CreatedAt : null, hasNextPage ? lastItem?.Id : null));
    }

    public async Task<Result<Unit, AppError>> MarkAsReadAsync(Id<InAppNotification> notificationId, Id<User> requestingUserId, CancellationToken cancellationToken = default)
    {
        var notification = await _inAppNotificationRepository.GetByIdAsync(notificationId, cancellationToken);
        if (notification == null)
        {
            return Result<Unit, AppError>.Failure(new InAppNotificationNotFoundError());
        }

        if (notification.RecipientId != requestingUserId)
        {
            return Result<Unit, AppError>.Failure(new InAppNotificationForbiddenError());
        }

        await _inAppNotificationRepository.MarkAsReadAsync(notificationId, cancellationToken);
        await _unitOfWork.SaveChangesAsync(cancellationToken);

        return Result<Unit, AppError>.Success(Unit.Value);
    }

    public async Task<Result<Unit, AppError>> MarkAllAsReadAsync(Id<User> userId, DateTimeOffset? before, CancellationToken cancellationToken = default)
    {
        await _inAppNotificationRepository.MarkAllAsReadAsync(userId, before, cancellationToken);
        await _unitOfWork.SaveChangesAsync(cancellationToken);

        return Result<Unit, AppError>.Success(Unit.Value);
    }

    public async Task<Result<int, AppError>> GetUnreadCountAsync(Id<User> userId, CancellationToken cancellationToken = default)
    {
        var count = await _inAppNotificationRepository.GetUnreadCountAsync(userId, cancellationToken);
        return Result<int, AppError>.Success(count);
    }

    private static InAppNotificationResult MapToResult(InAppNotification notification)
        => new(notification.Id, notification.RecipientId, notification.Message, notification.RedirectUrl, notification.IsRead, notification.Type, notification.IsSystemNotification, notification.SenderUserId, notification.CreatedAt);

    private async Task<InAppNotificationResult?> TryResolveDuplicateDeliveryAsync(
        CreateInAppNotificationInput input,
        InAppNotification notification,
        Exception exception,
        CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(input.DeliveryKey) || !IsDeliveryKeyUniqueViolation(exception))
        {
            return null;
        }

        _inAppNotificationRepository.Detach(notification);

        var existing = await _inAppNotificationRepository.FindByDeliveryKeyAsync(
            input.RecipientId,
            input.Type,
            input.DeliveryKey,
            cancellationToken);

        if (existing == null)
        {
            return null;
        }

        _logger.LogInformation(
            "Skipped duplicate in-app notification for recipient {RecipientId} type {Type} deliveryKey {DeliveryKey}.",
            input.RecipientId,
            input.Type,
            input.DeliveryKey);

        return MapToResult(existing);
    }

    private static bool IsDeliveryKeyUniqueViolation(Exception exception)
    {
        const string indexName = "IX_in_app_notifications_RecipientId_Type_DeliveryKey";

        return string.Equals(exception.GetType().Name, "DbUpdateException", StringComparison.Ordinal)
            && exception.ToString().Contains(indexName, StringComparison.Ordinal);
    }
}
