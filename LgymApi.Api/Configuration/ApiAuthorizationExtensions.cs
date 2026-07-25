using LgymApi.Domain.Security;
using Microsoft.Extensions.DependencyInjection;

namespace LgymApi.Api.Configuration;

public static class ApiAuthorizationExtensions
{
    public static IServiceCollection AddApiAuthorizationPolicies(this IServiceCollection services)
    {
        services
            .AddAuthorizationBuilder()
            .AddPolicy(AuthConstants.Policies.AdminAccess, policy =>
                policy.RequireClaim(AuthConstants.PermissionClaimType, AuthConstants.Permissions.AdminAccess))
            .AddPolicy(AuthConstants.Policies.ManageUserRoles, policy =>
                policy.RequireClaim(AuthConstants.PermissionClaimType, AuthConstants.Permissions.ManageUserRoles))
            .AddPolicy(AuthConstants.Policies.ManageAppConfig, policy =>
                policy.RequireClaim(AuthConstants.PermissionClaimType, AuthConstants.Permissions.ManageAppConfig))
            .AddPolicy(AuthConstants.Policies.ManageGlobalExercises, policy =>
                policy.RequireClaim(AuthConstants.PermissionClaimType, AuthConstants.Permissions.ManageGlobalExercises))
            .AddPolicy(AuthConstants.Policies.TrainerAccess, policy =>
                policy.RequireClaim(AuthConstants.PermissionClaimType, AuthConstants.Permissions.TrainerAccess));

        return services;
    }
}
