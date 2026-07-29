using LgymApi.Application.BuildingBlocks.Errors;
using LgymApi.Application.BuildingBlocks.Results;
using LgymApi.Application.Features.User.Models;
using LgymApi.Domain.ValueObjects;
using LgymApi.Identity.Contracts;

namespace LgymApi.Application.Identity.Contracts.Accounts;

public interface IAuthenticatedAccountCompatibilityPort
{
    Task<Result<UserInfoResult, AppError>> CheckTokenAsync(
        Id<AccountReference> accountId,
        CancellationToken cancellationToken = default);

    Task<Result<Unit, AppError>> LogoutAsync(
        Id<AccountReference> accountId,
        Id<AccountSessionReference>? sessionId,
        CancellationToken cancellationToken = default);

    Task<Result<Unit, AppError>> DeleteAccountAsync(
        Id<AccountReference> accountId,
        CancellationToken cancellationToken = default);

    Task<Result<Unit, AppError>> ChangeVisibilityInRankingAsync(
        Id<AccountReference> accountId,
        bool isVisibleInRanking,
        CancellationToken cancellationToken = default);

    Task<Result<Unit, AppError>> UpdateTimeZoneAsync(
        Id<AccountReference> accountId,
        string preferredTimeZone,
        CancellationToken cancellationToken = default);
}
