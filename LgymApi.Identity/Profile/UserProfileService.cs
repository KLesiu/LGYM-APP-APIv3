using LgymApi.Application.BuildingBlocks.Errors;
using LgymApi.Application.Identity.Errors;
using LgymApi.Application.BuildingBlocks.Results;
using LgymApi.Application.Features.Tutorial;
using LgymApi.Application.Features.User.Models;
using LgymApi.Application.Identity.Contracts.Accounts;
using LgymApi.Application.Identity.Contracts.Profile;
using LgymApi.Application.Identity.Mapping;
using LgymApi.Application.Mapping.Core;
using LgymApi.Application.Options;
using LgymApi.Application.Repositories;
using LgymApi.Application.Services;
using LgymApi.Resources;
using LgymApi.Identity.Contracts;
using UserEntity = LgymApi.Domain.Entities.User;

namespace LgymApi.Application.Identity.Profile;

internal sealed class UserProfileService : IUserProfileService
{
    private readonly IUserRepository _userRepository;
    private readonly IRoleRepository _roleRepository;
    private readonly IRankService _rankService;
    private readonly IUnitOfWork _unitOfWork;
    private readonly IAccountPushInstallationCleanupPort _accountPushInstallationCleanupPort;
    private readonly AppDefaultsOptions _appDefaultsOptions;
    private readonly ITutorialService _tutorialService;
    private readonly IMapper _mapper;

    public UserProfileService(
        IUserRepository userRepository,
        IRoleRepository roleRepository,
        IRankService rankService,
        IUnitOfWork unitOfWork,
        IAccountPushInstallationCleanupPort accountPushInstallationCleanupPort,
        AppDefaultsOptions appDefaultsOptions,
        ITutorialService tutorialService,
        IMapper mapper)
    {
        _userRepository = userRepository;
        _roleRepository = roleRepository;
        _rankService = rankService;
        _unitOfWork = unitOfWork;
        _accountPushInstallationCleanupPort = accountPushInstallationCleanupPort;
        _appDefaultsOptions = appDefaultsOptions;
        _tutorialService = tutorialService;
        _mapper = mapper;
    }

    public async Task<Result<UserInfoResult, AppError>> CheckTokenAsync(
        UserEntity? currentUser,
        CancellationToken cancellationToken = default)
    {
        if (currentUser == null)
        {
            return Result<UserInfoResult, AppError>.Failure(new UserNotFoundError(Messages.DidntFind));
        }

        var nextRank = _rankService.GetNextRank(currentUser.ProfileRank);
        var roles = await _roleRepository.GetRoleNamesByUserIdAsync(currentUser.Id, cancellationToken);
        var permissionClaims = await _roleRepository.GetPermissionClaimsByUserIdAsync(currentUser.Id, cancellationToken);
        var hasActiveTutorials = await _tutorialService.HasActiveTutorialsAsync(currentUser.Id, cancellationToken);
        var mappingContext = _mapper.CreateContext();
        mappingContext.Set(IdentityUserMappingProfile.Keys.DefaultPreferredTimeZone, _appDefaultsOptions.PreferredTimeZone);
        mappingContext.Set(IdentityUserMappingProfile.Keys.Elo, 1000);
        mappingContext.Set(IdentityUserMappingProfile.Keys.NextRank, nextRank);
        mappingContext.Set(IdentityUserMappingProfile.Keys.Roles, roles);
        mappingContext.Set(IdentityUserMappingProfile.Keys.PermissionClaims, permissionClaims);
        mappingContext.Set(IdentityUserMappingProfile.Keys.HasActiveTutorials, hasActiveTutorials);

        return Result<UserInfoResult, AppError>.Success(_mapper.Map<UserEntity, UserInfoResult>(currentUser, mappingContext));
    }

    public async Task<Result<Unit, AppError>> DeleteAccountAsync(
        UserEntity? currentUser,
        CancellationToken cancellationToken = default)
    {
        if (currentUser == null)
        {
            return Result<Unit, AppError>.Failure(new UserNotFoundError(Messages.DidntFind));
        }

        await _accountPushInstallationCleanupPort.StageRemoveForAccountAsync(
            currentUser.Id.Rebind<AccountReference>(),
            cancellationToken);
        currentUser.Email = $"anonymized_{currentUser.Id}@example.com";
        currentUser.Name = $"anonymized_user_{currentUser.Id}";
        currentUser.IsDeleted = true;

        await _userRepository.UpdateAsync(currentUser, cancellationToken);
        await _unitOfWork.SaveChangesAsync(cancellationToken);
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
        await _userRepository.UpdateAsync(currentUser, cancellationToken);
        await _unitOfWork.SaveChangesAsync(cancellationToken);
        return Result<Unit, AppError>.Success(Unit.Value);
    }
}
