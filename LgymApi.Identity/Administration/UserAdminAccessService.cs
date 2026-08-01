using LgymApi.Application.Identity.Contracts.Administration;
using LgymApi.Application.Repositories;
using LgymApi.Domain.Security;
using LgymApi.Domain.ValueObjects;
using UserEntity = LgymApi.Domain.Entities.User;

namespace LgymApi.Application.Identity.Administration;

internal sealed class UserAdminAccessService : IUserAdminAccessService
{
    private readonly IRoleRepository _roleRepository;

    public UserAdminAccessService(IRoleRepository roleRepository)
    {
        _roleRepository = roleRepository;
    }

    public async Task<bool> IsAdminAsync(Id<UserEntity> userId, CancellationToken cancellationToken = default)
    {
        if (userId.IsEmpty)
        {
            return false;
        }

        return await _roleRepository.UserHasPermissionAsync(userId, AuthConstants.Permissions.AdminAccess, cancellationToken);
    }
}
