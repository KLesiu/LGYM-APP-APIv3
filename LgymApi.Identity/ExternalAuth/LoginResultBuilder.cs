using LgymApi.Application.BuildingBlocks.Errors;
using LgymApi.Application.BuildingBlocks.Results;
using LgymApi.Application.Features.Tutorial;
using LgymApi.Application.Features.User.Models;
using LgymApi.Application.Repositories;
using LgymApi.Application.Services;
using LgymApi.Domain.Entities;
using LgymApi.Domain.ValueObjects;
using LgymApi.Resources;

namespace LgymApi.Application.ExternalAuth;

internal sealed class LoginResultBuilder : ILoginResultBuilder
{
    private readonly IRankService _rankService;
    private readonly ITokenService _tokenService;
    private readonly ITutorialService _tutorialService;
    private readonly IUserRepository _userRepository;
    private readonly IUserSessionStore _userSessionStore;

    public LoginResultBuilder(
        IUserRepository userRepository,
        IUserSessionStore userSessionStore,
        ITokenService tokenService,
        IRankService rankService,
        ITutorialService tutorialService)
    {
        _userRepository = userRepository;
        _userSessionStore = userSessionStore;
        _tokenService = tokenService;
        _rankService = rankService;
        _tutorialService = tutorialService;
    }

    public async Task<Result<LoginResult, AppError>> BuildAsync(User user, string preferredTimeZone, CancellationToken cancellationToken)
    {
        var userWithRoles = await _userRepository.FindByIdWithRolesAsync(user.Id, cancellationToken);
        if (userWithRoles == null)
        {
            return Result<LoginResult, AppError>.Failure(new InternalServerError(Messages.UserLoadFailed));
        }

        var roles = userWithRoles.UserRoles.Select(ur => ur.Role.Name).OrderBy(name => name).ToList();
        var permissionClaims = userWithRoles.UserRoles
            .SelectMany(ur => ur.Role.RoleClaims)
            .Where(rc => rc.ClaimType == LgymApi.Domain.Security.AuthConstants.PermissionClaimType)
            .Select(rc => rc.ClaimValue)
            .Distinct()
            .OrderBy(value => value)
            .ToList();

        var session = await _userSessionStore.CreateSessionAsync(userWithRoles.Id, DateTimeOffset.UtcNow.AddDays(30), cancellationToken);

        var token = _tokenService.CreateToken(userWithRoles.Id, session.Id, session.Jti, roles, permissionClaims);
        var nextRank = _rankService.GetNextRank(userWithRoles.ProfileRank);
        var hasActiveTutorials = await _tutorialService.HasActiveTutorialsAsync(userWithRoles.Id, cancellationToken);

        return Result<LoginResult, AppError>.Success(new LoginResult
        {
            Token = token,
            PermissionClaims = permissionClaims,
            User = new UserInfoResult
            {
                Name = userWithRoles.Name,
                Id = userWithRoles.Id,
                Email = userWithRoles.Email,
                Avatar = userWithRoles.Avatar,
                ProfileRank = userWithRoles.ProfileRank,
                PreferredTimeZone = string.IsNullOrWhiteSpace(userWithRoles.PreferredTimeZone) ? preferredTimeZone : userWithRoles.PreferredTimeZone,
                CreatedAt = userWithRoles.CreatedAt.UtcDateTime,
                UpdatedAt = userWithRoles.UpdatedAt.UtcDateTime,
                Elo = 1000,
                NextRank = nextRank == null ? null : new RankInfo { Name = nextRank.Name, NeedElo = nextRank.NeedElo },
                IsDeleted = userWithRoles.IsDeleted,
                IsVisibleInRanking = userWithRoles.IsVisibleInRanking,
                Roles = roles,
                PermissionClaims = permissionClaims,
                HasActiveTutorials = hasActiveTutorials
            }
        });
    }
}
