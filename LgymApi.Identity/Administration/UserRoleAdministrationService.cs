using LgymApi.Application.BuildingBlocks.Errors;
using LgymApi.Application.Identity.Errors;
using LgymApi.Application.BuildingBlocks.Results;
using LgymApi.Application.Identity.Contracts.Administration;
using LgymApi.Application.Repositories;
using LgymApi.Application.Services;
using LgymApi.Domain.ValueObjects;
using LgymApi.Resources;
using UserEntity = LgymApi.Domain.Entities.User;

namespace LgymApi.Application.Identity.Administration;

internal sealed class UserRoleAdministrationService : IUserRoleAdministrationService
{
    private readonly IUserRepository _userRepository;
    private readonly IRoleRepository _roleRepository;
    private readonly IUnitOfWork _unitOfWork;

    public UserRoleAdministrationService(
        IUserRepository userRepository,
        IRoleRepository roleRepository,
        IUnitOfWork unitOfWork)
    {
        _userRepository = userRepository;
        _roleRepository = roleRepository;
        _unitOfWork = unitOfWork;
    }

    public async Task<Result<Unit, AppError>> UpdateUserRolesAsync(
        Id<UserEntity> targetUserId,
        IReadOnlyCollection<string> roles,
        CancellationToken cancellationToken = default)
    {
        if (targetUserId.IsEmpty)
        {
            return Result<Unit, AppError>.Failure(new InvalidUserError(Messages.FieldRequired));
        }

        var user = await _userRepository.FindByIdAsync(targetUserId, cancellationToken);
        if (user == null)
        {
            return Result<Unit, AppError>.Failure(new UserNotFoundError(Messages.DidntFind));
        }

        var normalizedRoleNames = roles
            .Where(role => !string.IsNullOrWhiteSpace(role))
            .Select(role => role.Trim())
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToList();

        var rolesToSet = await _roleRepository.GetByNamesAsync(normalizedRoleNames, cancellationToken);
        if (rolesToSet.Count != normalizedRoleNames.Count)
        {
            return Result<Unit, AppError>.Failure(new InvalidUserError(Messages.InvalidRoleSelection));
        }

        await _roleRepository.ReplaceUserRolesAsync(targetUserId, rolesToSet.Select(role => role.Id).ToList(), cancellationToken);
        await _unitOfWork.SaveChangesAsync(cancellationToken);

        return Result<Unit, AppError>.Success(Unit.Value);
    }
}
