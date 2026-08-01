using LgymApi.Domain.ValueObjects;
using LgymApi.Identity.Contracts;
using LgymApi.Resources;

namespace LgymApi.Api.Middleware;

public static class RouteUserAccessGuard
{
    public static Id<AccountReference> ParseRouteAccountIdForCurrentAccount(this HttpContext context, string routeUserId)
    {
        var currentAccountId = context.GetCurrentAccountId();
        if (currentAccountId.IsEmpty)
        {
            throw new UnauthorizedAccessException(Messages.Forbidden);
        }


        if (!Id<AccountReference>.TryParse(routeUserId, out var parsedRouteUserId))
        {
            throw new UnauthorizedAccessException(Messages.Forbidden);
        }

        if (parsedRouteUserId != currentAccountId)
        {
            throw new UnauthorizedAccessException(Messages.Forbidden);
        }

        return parsedRouteUserId;
    }

    public static Id<AccountReference> ParseRouteAccountIdForCurrentAdmin(this HttpContext context, string routeUserId)
    {
        var currentAccountId = context.GetCurrentAccountId();
        if (currentAccountId.IsEmpty)
        {
            throw new UnauthorizedAccessException(Messages.Forbidden);
        }

        if (!Id<AccountReference>.TryParse(routeUserId, out var parsedRouteUserId))
        {
            throw new UnauthorizedAccessException(Messages.Forbidden);
        }

        if (parsedRouteUserId != currentAccountId)
        {
            throw new UnauthorizedAccessException(Messages.Forbidden);
        }

        return parsedRouteUserId;
    }
}
