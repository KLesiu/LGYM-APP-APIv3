using LgymApi.Api.Middleware;
using Microsoft.AspNetCore.Authorization;

namespace LgymApi.Api.Authorization;

public sealed record CurrentPermissionRequirement(string Permission) : IAuthorizationRequirement;

internal sealed class CurrentPermissionAuthorizationHandler : AuthorizationHandler<CurrentPermissionRequirement>
{
    protected override Task HandleRequirementAsync(
        AuthorizationHandlerContext context,
        CurrentPermissionRequirement requirement)
    {
        if (context.Resource is HttpContext httpContext
            && httpContext.Features.Get<IAuthenticatedAccountContextFeature>() is { } accountFeature
            && accountFeature.Context.PermissionClaims.Contains(requirement.Permission, StringComparer.Ordinal))
        {
            context.Succeed(requirement);
        }

        return Task.CompletedTask;
    }
}
