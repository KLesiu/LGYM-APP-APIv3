using LgymApi.Application.Platform.ReferenceData.AppConfig.Contracts;
using LgymApi.Application.Repositories;
using LgymApi.Domain.Entities;
using LgymApi.Domain.Security;
using LgymApi.Domain.ValueObjects;
using LgymApi.Platform.Contracts;

namespace LgymApi.Application.Identity.Adapters;

internal sealed class AppConfigAuthorizationAdapter(
    IUserRepository userRepository,
    IRoleRepository roleRepository) : IAppConfigAuthorizationPort
{
    public async Task<bool> CanManageAppConfigAsync(
        Id<ActorReference> actorId,
        CancellationToken cancellationToken = default)
    {
        var userId = actorId.Rebind<User>();

        if (userId.IsEmpty)
        {
            return false;
        }

        var user = await userRepository.FindByIdAsync(userId, cancellationToken);
        return user != null
               && await roleRepository.UserHasPermissionAsync(
                   userId,
                   AuthConstants.Permissions.ManageAppConfig,
                   cancellationToken);
    }
}
