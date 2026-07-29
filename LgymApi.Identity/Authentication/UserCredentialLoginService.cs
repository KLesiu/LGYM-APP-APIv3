using LgymApi.Application.BuildingBlocks.Errors;
using LgymApi.Application.Identity.Errors;
using LgymApi.Application.BuildingBlocks.Results;
using LgymApi.Application.Features.Tutorial;
using LgymApi.Application.Features.User.Models;
using LgymApi.Application.Identity.Contracts.Authentication;
using LgymApi.Application.Identity.Mapping;
using LgymApi.Application.Mapping.Core;
using LgymApi.Application.Options;
using LgymApi.Application.Repositories;
using LgymApi.Application.Services;
using LgymApi.Domain.Security;
using LgymApi.Domain.ValueObjects;
using LgymApi.Resources;
using UserEntity = LgymApi.Domain.Entities.User;

namespace LgymApi.Application.Identity.Authentication;

internal sealed class UserCredentialLoginService : IUserCredentialLoginService
{
    private readonly IUserRepository _userRepository;
    private readonly IRoleRepository _roleRepository;
    private readonly ILegacyPasswordService _legacyPasswordService;
    private readonly IRankService _rankService;
    private readonly IUserSessionStore _userSessionStore;
    private readonly ITokenService _tokenService;
    private readonly IUnitOfWork _unitOfWork;
    private readonly AppDefaultsOptions _appDefaultsOptions;
    private readonly ITutorialService _tutorialService;
    private readonly IMapper _mapper;

    public UserCredentialLoginService(UserCredentialLoginServiceDependencies dependencies)
    {
        _userRepository = dependencies.UserRepository;
        _roleRepository = dependencies.RoleRepository;
        _legacyPasswordService = dependencies.LegacyPasswordService;
        _rankService = dependencies.RankService;
        _userSessionStore = dependencies.UserSessionStore;
        _tokenService = dependencies.TokenService;
        _unitOfWork = dependencies.UnitOfWork;
        _appDefaultsOptions = dependencies.AppDefaultsOptions;
        _tutorialService = dependencies.TutorialService;
        _mapper = dependencies.Mapper;
    }

    public Task<Result<LoginResult, AppError>> LoginAsync(
        string name,
        string password,
        CancellationToken cancellationToken = default)
    {
        return LoginCoreAsync(name, password, requiredRole: null, cancellationToken);
    }

    public Task<Result<LoginResult, AppError>> LoginTrainerAsync(
        string name,
        string password,
        CancellationToken cancellationToken = default)
    {
        return LoginCoreAsync(name, password, AuthConstants.Roles.Trainer, cancellationToken);
    }

    private async Task<Result<LoginResult, AppError>> LoginCoreAsync(
        string name,
        string password,
        string? requiredRole,
        CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(name) || string.IsNullOrWhiteSpace(password))
        {
            return Result<LoginResult, AppError>.Failure(new UserUnauthorizedError(Messages.Unauthorized));
        }

        var user = await _userRepository.FindByNameAsync(name, cancellationToken);
        if (user == null || string.IsNullOrWhiteSpace(user.LegacyHash) || string.IsNullOrWhiteSpace(user.LegacySalt))
        {
            return Result<LoginResult, AppError>.Failure(new UserUnauthorizedError(Messages.Unauthorized));
        }

        var valid = _legacyPasswordService.Verify(
            password,
            user.LegacyHash,
            user.LegacySalt,
            user.LegacyIterations,
            user.LegacyKeyLength,
            user.LegacyDigest);

        if (!valid)
        {
            return Result<LoginResult, AppError>.Failure(new UserUnauthorizedError(Messages.Unauthorized));
        }

        var roles = await _roleRepository.GetRoleNamesByUserIdAsync(user.Id, cancellationToken);
        if (!string.IsNullOrWhiteSpace(requiredRole) && !roles.Contains(requiredRole, StringComparer.Ordinal))
        {
            return Result<LoginResult, AppError>.Failure(new UserUnauthorizedError(Messages.Unauthorized));
        }

        var permissionClaims = await _roleRepository.GetPermissionClaimsByUserIdAsync(user.Id, cancellationToken);
        var session = await _userSessionStore.CreateSessionAsync(user.Id, DateTimeOffset.UtcNow.AddDays(30), cancellationToken);
        await _unitOfWork.SaveChangesAsync(cancellationToken);

        var token = _tokenService.CreateToken(user.Id, session.Id, session.Jti, roles, permissionClaims);
        var nextRank = _rankService.GetNextRank(user.ProfileRank);
        var hasActiveTutorials = await _tutorialService.HasActiveTutorialsAsync(user.Id, cancellationToken);
        var mappingContext = _mapper.CreateContext();
        mappingContext.Set(IdentityUserMappingProfile.Keys.DefaultPreferredTimeZone, _appDefaultsOptions.PreferredTimeZone);
        mappingContext.Set(IdentityUserMappingProfile.Keys.Elo, 1000);
        mappingContext.Set(IdentityUserMappingProfile.Keys.NextRank, nextRank);
        mappingContext.Set(IdentityUserMappingProfile.Keys.Roles, roles);
        mappingContext.Set(IdentityUserMappingProfile.Keys.PermissionClaims, permissionClaims);
        mappingContext.Set(IdentityUserMappingProfile.Keys.HasActiveTutorials, hasActiveTutorials);

        return Result<LoginResult, AppError>.Success(new LoginResult
        {
            Token = token,
            PermissionClaims = permissionClaims,
            User = _mapper.Map<UserEntity, UserInfoResult>(user, mappingContext)
        });
    }
}
