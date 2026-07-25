using LgymApi.Application.BuildingBlocks.Errors;
using LgymApi.Application.Identity.Errors;
using LgymApi.Application.BuildingBlocks.Results;
using LgymApi.Application.Features.User.Models;
using LgymApi.Application.Identity.Contracts.Profile;
using LgymApi.Application.Identity.Mapping;
using LgymApi.Resources;
using UserEntity = LgymApi.Domain.Entities.User;

namespace LgymApi.Application.Identity.Profile;

public sealed class UserProfileService : IUserProfileService
{
    private readonly UserProfileServiceDependencies _dependencies;

    public UserProfileService(UserProfileServiceDependencies dependencies)
    {
        _dependencies = dependencies;
    }

    public async Task<Result<UserInfoResult, AppError>> CheckTokenAsync(
        UserEntity? currentUser,
        CancellationToken cancellationToken = default)
    {
        if (currentUser == null)
        {
            return Result<UserInfoResult, AppError>.Failure(new UserNotFoundError(Messages.DidntFind));
        }

        var nextRank = _dependencies.RankService.GetNextRank(currentUser.ProfileRank);
        var roles = await _dependencies.RoleRepository.GetRoleNamesByUserIdAsync(currentUser.Id, cancellationToken);
        var permissionClaims = await _dependencies.RoleRepository.GetPermissionClaimsByUserIdAsync(currentUser.Id, cancellationToken);
        var hasActiveTutorials = await _dependencies.TutorialService.HasActiveTutorialsAsync(currentUser.Id, cancellationToken);
        var mappingContext = _dependencies.Mapper.CreateContext();
        mappingContext.Set(IdentityUserMappingProfile.Keys.DefaultPreferredTimeZone, _dependencies.AppDefaultsOptions.PreferredTimeZone);
        mappingContext.Set(IdentityUserMappingProfile.Keys.Elo, 1000);
        mappingContext.Set(IdentityUserMappingProfile.Keys.NextRank, nextRank);
        mappingContext.Set(IdentityUserMappingProfile.Keys.Roles, roles);
        mappingContext.Set(IdentityUserMappingProfile.Keys.PermissionClaims, permissionClaims);
        mappingContext.Set(IdentityUserMappingProfile.Keys.HasActiveTutorials, hasActiveTutorials);

        return Result<UserInfoResult, AppError>.Success(_dependencies.Mapper.Map<UserEntity, UserInfoResult>(currentUser, mappingContext));
    }

    public async Task<Result<Unit, AppError>> DeleteAccountAsync(
        UserEntity? currentUser,
        CancellationToken cancellationToken = default)
    {
        if (currentUser == null)
        {
            return Result<Unit, AppError>.Failure(new UserNotFoundError(Messages.DidntFind));
        }

        currentUser.Email = $"anonymized_{currentUser.Id}@example.com";
        currentUser.Name = $"anonymized_user_{currentUser.Id}";
        currentUser.IsDeleted = true;

        await _dependencies.UserRepository.UpdateAsync(currentUser, cancellationToken);
        await _dependencies.UnitOfWork.SaveChangesAsync(cancellationToken);
        return Result<Unit, AppError>.Success(Unit.Value);
    }

    public async Task<Result<Unit, AppError>> UpdateTimeZoneAsync(
        UserEntity? currentUser,
        string preferredTimeZone,
        CancellationToken cancellationToken = default)
    {
        if (currentUser == null)
        {
            return Result<Unit, AppError>.Failure(new InvalidUserError(Messages.DidntFind));
        }

        if (string.IsNullOrWhiteSpace(preferredTimeZone))
        {
            return Result<Unit, AppError>.Failure(new InvalidUserError(Messages.FieldRequired));
        }

        var normalizedPreferredTimeZone = preferredTimeZone.Trim();
        try
        {
            _ = TimeZoneInfo.FindSystemTimeZoneById(normalizedPreferredTimeZone);
        }
        catch (TimeZoneNotFoundException)
        {
            return Result<Unit, AppError>.Failure(new InvalidUserError(Messages.InvalidTimeZone));
        }
        catch (InvalidTimeZoneException)
        {
            return Result<Unit, AppError>.Failure(new InvalidUserError(Messages.InvalidTimeZone));
        }

        currentUser.PreferredTimeZone = normalizedPreferredTimeZone;
        await _dependencies.UserRepository.UpdateAsync(currentUser, cancellationToken);
        await _dependencies.UnitOfWork.SaveChangesAsync(cancellationToken);
        return Result<Unit, AppError>.Success(Unit.Value);
    }
}
