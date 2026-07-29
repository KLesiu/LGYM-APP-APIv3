using LgymApi.Application.Identity.Contracts.Access;
using LgymApi.Application.Repositories;
using LgymApi.Domain.Security;
using LgymApi.Domain.ValueObjects;

namespace LgymApi.Application.Identity.Access;

internal sealed class UserAccessReadService : IUserAccessReadService
{
    private readonly IUserRepository _userRepository;
    private readonly IRoleRepository _roleRepository;

    public UserAccessReadService(IUserRepository userRepository, IRoleRepository roleRepository)
    {
        _userRepository = userRepository;
        _roleRepository = roleRepository;
    }

    public async Task<bool> UserExistsAsync(Id<LgymApi.Domain.Entities.User> userId, CancellationToken cancellationToken = default)
        => !userId.IsEmpty && await _userRepository.FindByIdAsync(userId, cancellationToken) != null;

    public Task<bool> IsTrainerAsync(Id<LgymApi.Domain.Entities.User> userId, CancellationToken cancellationToken = default)
        => userId.IsEmpty
            ? Task.FromResult(false)
            : _roleRepository.UserHasRoleAsync(userId, AuthConstants.Roles.Trainer, cancellationToken);
}
