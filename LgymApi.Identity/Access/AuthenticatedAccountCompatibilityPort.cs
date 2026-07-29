using LgymApi.Application.BuildingBlocks.Errors;
using LgymApi.Application.BuildingBlocks.Results;
using LgymApi.Application.Features.User.Models;
using LgymApi.Application.Identity.Contracts.Accounts;
using LgymApi.Application.Identity.Contracts.Profile;
using LgymApi.Application.Identity.Contracts.Ranking;
using LgymApi.Application.Identity.Contracts.Sessions;
using LgymApi.Application.Repositories;
using LgymApi.Domain.Entities;
using LgymApi.Domain.ValueObjects;
using LgymApi.Identity.Contracts;

namespace LgymApi.Application.Identity.Access;

internal sealed class AuthenticatedAccountCompatibilityPort(
    IUserRepository userRepository,
    IUserProfileService userProfileService,
    IUserSessionTerminationService userSessionTerminationService,
    IUserRankingService userRankingService) : IAuthenticatedAccountCompatibilityPort
{
    public async Task<Result<UserInfoResult, AppError>> CheckTokenAsync(
        Id<AccountReference> accountId,
        CancellationToken cancellationToken = default)
        => await userProfileService.CheckTokenAsync(await LoadAsync(accountId, cancellationToken), cancellationToken);

    public async Task<Result<Unit, AppError>> LogoutAsync(
        Id<AccountReference> accountId,
        Id<AccountSessionReference>? sessionId,
        CancellationToken cancellationToken = default)
        => await userSessionTerminationService.LogoutAsync(
            await LoadAsync(accountId, cancellationToken),
            sessionId?.Rebind<UserSession>(),
            cancellationToken);

    public async Task<Result<Unit, AppError>> DeleteAccountAsync(
        Id<AccountReference> accountId,
        CancellationToken cancellationToken = default)
        => await userProfileService.DeleteAccountAsync(await LoadAsync(accountId, cancellationToken), cancellationToken);

    public async Task<Result<Unit, AppError>> ChangeVisibilityInRankingAsync(
        Id<AccountReference> accountId,
        bool isVisibleInRanking,
        CancellationToken cancellationToken = default)
        => await userRankingService.ChangeVisibilityInRankingAsync(
            await LoadAsync(accountId, cancellationToken),
            isVisibleInRanking,
            cancellationToken);

    public async Task<Result<Unit, AppError>> UpdateTimeZoneAsync(
        Id<AccountReference> accountId,
        string preferredTimeZone,
        CancellationToken cancellationToken = default)
        => await userProfileService.UpdateTimeZoneAsync(
            await LoadAsync(accountId, cancellationToken),
            preferredTimeZone,
            cancellationToken);

    private Task<User?> LoadAsync(Id<AccountReference> accountId, CancellationToken cancellationToken)
        => accountId.IsEmpty
            ? Task.FromResult<User?>(null)
            : userRepository.FindByIdAsync(accountId.Rebind<User>(), cancellationToken);
}
