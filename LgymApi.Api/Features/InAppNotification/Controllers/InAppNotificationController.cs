using LgymApi.Api.Extensions;
using LgymApi.Api.Features.Common.Contracts;
using LgymApi.Api.Features.InAppNotification.Contracts;
using LgymApi.Api.Middleware;
using LgymApi.Application.Mapping.Core;
using LgymApi.Application.Notifications.Models;
using LgymApi.Notifications.ApiAdapters;
using NotificationEntity = global::LgymApi.Domain.Entities.InAppNotification;
using LgymApi.Resources;
using Microsoft.AspNetCore.Mvc;

namespace LgymApi.Api.Features.InAppNotification.Controllers;

[ApiController]
[Route("api")]
public sealed class InAppNotificationController : ControllerBase
{
    private readonly IInAppNotificationApiAdapter _notificationService;
    private readonly IMapper _mapper;

    public InAppNotificationController(IInAppNotificationApiAdapter notificationService, IMapper mapper)
    {
        _notificationService = notificationService;
        _mapper = mapper;
    }

    [HttpGet("{id}/notifications")]
    [ProducesResponseType(typeof(PagedNotificationsResultDto), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ResponseMessageDto), StatusCodes.Status403Forbidden)]
    public async Task<IActionResult> GetNotifications(
        [FromRoute] string id,
        [FromQuery] GetNotificationsQueryDto query,
        CancellationToken cancellationToken = default)
    {
        var accountId = HttpContext.ParseRouteAccountIdForCurrentAccount(id);
        var cursorQuery = _mapper.Map<GetNotificationsQueryDto, CursorPaginationQuery>(query);
        var result = await _notificationService.GetForAccountAsync(accountId, cursorQuery, cancellationToken);
        if (result.IsFailure)
        {
            return result.ToActionResult();
        }

        var mapped = _mapper.Map<PagedResult<InAppNotificationResult>, PagedNotificationsResultDto>(result.Value);
        return Ok(mapped);
    }

    [HttpPost("{id}/notifications/{notificationId}/mark-read")]
    [ProducesResponseType(typeof(ResponseMessageDto), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ResponseMessageDto), StatusCodes.Status403Forbidden)]
    [ProducesResponseType(typeof(ResponseMessageDto), StatusCodes.Status404NotFound)]
    public async Task<IActionResult> MarkAsRead(
        [FromRoute] string id,
        [FromRoute] string notificationId,
        CancellationToken cancellationToken = default)
    {
        var accountId = HttpContext.ParseRouteAccountIdForCurrentAccount(id);
        var notifId = notificationId.ToIdOrEmpty<NotificationEntity>();
        var result = await _notificationService.MarkAsReadAsync(notifId, accountId, cancellationToken);
        if (result.IsFailure)
        {
            return result.ToActionResult();
        }

        return Ok(_mapper.Map<string, ResponseMessageDto>("Notification marked as read"));
    }

    [HttpPost("{id}/notifications/mark-all-read")]
    [ProducesResponseType(typeof(ResponseMessageDto), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ResponseMessageDto), StatusCodes.Status403Forbidden)]
    public async Task<IActionResult> MarkAllAsRead(
        [FromRoute] string id,
        [FromQuery] DateTimeOffset? before,
        CancellationToken cancellationToken = default)
    {
        var accountId = HttpContext.ParseRouteAccountIdForCurrentAccount(id);
        var result = await _notificationService.MarkAllAsReadAsync(accountId, before, cancellationToken);
        if (result.IsFailure)
        {
            return result.ToActionResult();
        }

        return Ok(_mapper.Map<string, ResponseMessageDto>("All notifications marked as read"));
    }

    [HttpGet("{id}/notifications/unread-count")]
    [ProducesResponseType(typeof(UnreadCountDto), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ResponseMessageDto), StatusCodes.Status403Forbidden)]
    public async Task<IActionResult> GetUnreadCount([FromRoute] string id, CancellationToken cancellationToken = default)
    {
        var accountId = HttpContext.ParseRouteAccountIdForCurrentAccount(id);
        var result = await _notificationService.GetUnreadCountAsync(accountId, cancellationToken);
        if (result.IsFailure)
        {
            return result.ToActionResult();
        }

        return Ok(_mapper.Map<int, UnreadCountDto>(result.Value));
    }
}
