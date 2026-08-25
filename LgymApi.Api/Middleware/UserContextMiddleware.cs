using LgymApi.Domain.Security;
using LgymApi.Domain.ValueObjects;
using LgymApi.Identity.Contracts;
using LgymApi.Identity.Contracts.Accounts;
using LgymApi.Identity.Contracts.AdultConfirmation;
using LgymApi.Api.AgeGate;
using Microsoft.AspNetCore.Authorization;
using Microsoft.Extensions.Options;

namespace LgymApi.Api.Middleware;

public sealed class UserContextMiddleware
{
    private readonly RequestDelegate _next;

    public UserContextMiddleware(RequestDelegate next)
    {
        _next = next;
    }

    public async Task InvokeAsync(
        HttpContext context,
        IAuthenticatedAccountContextResolver authenticatedAccountContextResolver,
        IOptions<AgeGateOptions> ageGateOptions)
    {
        var endpoint = context.GetEndpoint();
        if (endpoint?.Metadata.GetMetadata<IAllowAnonymous>() != null)
        {
            await _next(context);
            return;
        }

        var sidClaim = context.User.FindFirst(AuthConstants.ClaimNames.SessionId)?.Value;
        if (string.IsNullOrWhiteSpace(sidClaim) || !Id<AccountSessionReference>.TryParse(sidClaim, out var sessionId))
        {
            await ErrorResponseWriter.WriteAsync(context, StatusCodes.Status401Unauthorized, Messages.InvalidToken, context.RequestAborted);
            return;
        }

        var userIdClaim = context.User.FindFirst(AuthConstants.ClaimNames.UserId)?.Value;
        if (string.IsNullOrWhiteSpace(userIdClaim) || !Id<AccountReference>.TryParse(userIdClaim, out var accountId))
        {
            await ErrorResponseWriter.WriteAsync(context, StatusCodes.Status401Unauthorized, Messages.InvalidToken, context.RequestAborted);
            return;
        }

        var resolution = await authenticatedAccountContextResolver.ResolveAsync(accountId, sessionId, context.RequestAborted);
        if (resolution.Status == AuthenticatedAccountResolutionStatus.SessionInvalid)
        {
            await ErrorResponseWriter.WriteAsync(context, StatusCodes.Status401Unauthorized, Messages.Unauthorized, context.RequestAborted);
            return;
        }

        if (resolution.Status == AuthenticatedAccountResolutionStatus.AccountNotFound)
        {
            await ErrorResponseWriter.WriteAsync(context, StatusCodes.Status401Unauthorized, Messages.InvalidToken, context.RequestAborted);
            return;
        }

        if (resolution.Status == AuthenticatedAccountResolutionStatus.AccountDeleted)
        {
            await ErrorResponseWriter.WriteAsync(context, StatusCodes.Status401Unauthorized, Messages.Unauthorized, context.RequestAborted);
            return;
        }

        if (resolution.Status == AuthenticatedAccountResolutionStatus.AccountBlocked)
        {
            await ErrorResponseWriter.WriteAsync(context, StatusCodes.Status403Forbidden, Messages.AccountBlocked, context.RequestAborted);
            return;
        }

        var ageGate = ageGateOptions.Value;
        var ageGateAllowed = endpoint?.Metadata.GetMetadata<AllowAgeGatedAttribute>() != null;
        if (ageGate.Enabled && resolution.Context?.AdultConfirmedAt is null && !ageGateAllowed)
        {
            await ErrorResponseWriter.WriteAsync(
                context,
                StatusCodes.Status428PreconditionRequired,
                Messages.AdultConfirmationRequired,
                context.RequestAborted,
                "AdultConfirmationRequired");
            return;
        }

        context.Features.Set<IAuthenticatedAccountContextFeature>(new AuthenticatedAccountContextFeature(resolution.Context!));
        await _next(context);
    }
}
