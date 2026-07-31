using LgymApi.Api.Features.Common.Contracts;
using LgymApi.Api.Features.InAppNotification.Contracts;
using LgymApi.Api.Middleware;
using LgymApi.Application.Mapping.Core;
using LgymApi.Notifications.ApiAdapters;
using LgymApi.Domain.Security;
using LgymApi.Identity.Contracts;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using InAppNotificationEntity = global::LgymApi.Domain.Entities.InAppNotification;

namespace LgymApi.Api.Features.InAppNotification.Controllers;

[ApiController]
[Route("api/internal/push")]
[Authorize(Policy = AuthConstants.Policies.AdminAccess)]
public sealed class PushNotificationAdminController : ControllerBase
{
    private const int SchemaVersion = 1;

    private readonly INotificationEventApiAdapter _notificationEventApiAdapter;
    private readonly IMapper _mapper;

    public PushNotificationAdminController(INotificationEventApiAdapter notificationEventApiAdapter, IMapper mapper)
    {
        _notificationEventApiAdapter = notificationEventApiAdapter;
        _mapper = mapper;
    }

    [HttpPost("test-event")]
    [ProducesResponseType(typeof(ResponseMessageDto), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ResponseMessageDto), StatusCodes.Status403Forbidden)]
    public async Task<IActionResult> EnqueueTestEvent(
        [FromBody] EnqueueTestPushEventRequest request,
        CancellationToken cancellationToken = default)
    {
        var input = new EnqueueAccountNotificationEventInput(
            request.RecipientUserId.ToIdOrEmpty<AccountReference>(),
            SchemaVersion,
            request.Type,
            request.EventId,
            request.EntityId,
            request.InAppNotificationId.ToNullableId<InAppNotificationEntity>(),
            request.Deeplink);

        await _notificationEventApiAdapter.EnqueueAsync(input, cancellationToken);

        return Ok(_mapper.Map<string, ResponseMessageDto>("Push test event queued"));
    }
}
