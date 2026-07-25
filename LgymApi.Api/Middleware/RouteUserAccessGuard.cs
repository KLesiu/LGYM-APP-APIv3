using LgymApi.Domain.Entities;
using LgymApi.Domain.ValueObjects;
using LgymApi.Resources;

namespace LgymApi.Api.Middleware;

public static class RouteUserAccessGuard
{
    public static Id<User> ParseRouteUserIdForCurrentUser(this HttpContext context, string routeUserId)
    {
        var currentUser = context.GetCurrentUser();
        if (currentUser == null || currentUser.Id.IsEmpty)
        {
            throw new UnauthorizedAccessException(Messages.Forbidden);
        }


        if (!Id<User>.TryParse(routeUserId, out var parsedRouteUserId))
        {
            throw new UnauthorizedAccessException(Messages.Forbidden);
        }

        if (parsedRouteUserId != currentUser.Id)
        {
            throw new UnauthorizedAccessException(Messages.Forbidden);
        }

        return parsedRouteUserId;
    }

    public static Id<User> ParseRouteUserIdForCurrentAdmin(this HttpContext context, string routeUserId)
    {
        var currentUser = context.GetCurrentUser();
        if (currentUser == null || currentUser.Id.IsEmpty)
        {
            throw new UnauthorizedAccessException(Messages.Forbidden);
        }

        if (!Id<User>.TryParse(routeUserId, out var parsedRouteUserId))
        {
            throw new UnauthorizedAccessException(Messages.Forbidden);
        }

        if (parsedRouteUserId != currentUser.Id)
        {
            throw new UnauthorizedAccessException(Messages.Forbidden);
        }

        return parsedRouteUserId;
    }
}
