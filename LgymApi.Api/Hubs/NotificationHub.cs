using LgymApi.Domain.Security;
using LgymApi.Domain.ValueObjects;
using LgymApi.Identity.Contracts;
using LgymApi.Identity.Contracts.Accounts;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.SignalR;
using Microsoft.Extensions.Logging;

namespace LgymApi.Api.Hubs;

[Authorize]
public sealed class NotificationHub : Hub
{
    private readonly IAuthenticatedAccountContextResolver _authenticatedAccountContextResolver;
    private readonly ILogger<NotificationHub> _logger;

    public NotificationHub(
        IAuthenticatedAccountContextResolver authenticatedAccountContextResolver,
        ILogger<NotificationHub> logger)
    {
        _authenticatedAccountContextResolver = authenticatedAccountContextResolver;
        _logger = logger;
    }

    public override async Task OnConnectedAsync()
    {
        var sidClaim = Context.User?.FindFirst(AuthConstants.ClaimNames.SessionId)?.Value;
        if (sidClaim == null || !Id<AccountSessionReference>.TryParse(sidClaim, out var sessionId))
        {
            Context.Abort();
            return;
        }

        var userId = Context.User?.FindFirst(AuthConstants.ClaimNames.UserId)?.Value;
        if (userId == null || !Id<AccountReference>.TryParse(userId, out var accountId))
        {
            Context.Abort();
            return;
        }

        var resolution = await _authenticatedAccountContextResolver.ResolveAsync(accountId, sessionId, Context.ConnectionAborted);
        if (resolution.Status != AuthenticatedAccountResolutionStatus.Active)
        {
            Context.Abort();
            return;
        }

        await Groups.AddToGroupAsync(Context.ConnectionId, $"user-{resolution.Context!.Id}");
        await base.OnConnectedAsync();
    }

    public override async Task OnDisconnectedAsync(Exception? exception)
    {
        _logger.LogInformation(
            exception,
            "Notification hub disconnected for connection {ConnectionId}",
            Context.ConnectionId);

        await base.OnDisconnectedAsync(exception);
    }

    // Push-only hub — no client-to-server methods
}
