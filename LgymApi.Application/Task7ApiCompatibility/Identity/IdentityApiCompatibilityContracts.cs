using LgymApi.Application.BuildingBlocks.Errors;
using LgymApi.Application.BuildingBlocks.Results;
using LgymApi.Application.Features.AdminManagement.Models;
using LgymApi.Application.Pagination;
using LgymApi.Domain.Enums;
using LgymApi.Domain.ValueObjects;
using LgymApi.Identity.Contracts;

namespace LgymApi.Application.Identity.ApiCompatibility;

public sealed record AccountRankProjection(string Name, int NeedElo);

public sealed record AccountProfileProjection(
    Id<AccountReference> Id,
    string Name,
    string Email,
    string? Avatar,
    string ProfileRank,
    string PreferredTimeZone,
    DateTime CreatedAt,
    DateTime UpdatedAt,
    int Elo,
    AccountRankProjection? NextRank,
    bool IsDeleted,
    bool IsVisibleInRanking,
    IReadOnlyList<string> Roles,
    IReadOnlyList<string> PermissionClaims,
    bool HasActiveTutorials);

public sealed record ExternalLoginProjection(string Provider, string? ProviderEmail);

public sealed record TutorialProgressProjection(
    string Id,
    TutorialType TutorialType,
    string TutorialName,
    string TutorialDescription,
    bool IsCompleted,
    DateTime? CompletedAt,
    IReadOnlyList<TutorialStep> CompletedSteps,
    IReadOnlyList<TutorialStep> RemainingSteps,
    int TotalSteps,
    int CompletedStepsCount);

public sealed record AdminAccountProjection(
    Id<AccountReference> Id,
    string Name,
    string Email,
    string? Avatar,
    string ProfileRank,
    bool IsVisibleInRanking,
    bool IsBlocked,
    bool IsDeleted,
    DateTimeOffset CreatedAt,
    DateTimeOffset? UpdatedAt,
    IReadOnlyList<string> Roles);

public sealed record RoleProjection(
    Id<RoleReference> Id,
    string Name,
    string? Description,
    IReadOnlyList<string> PermissionClaims);

public sealed record PermissionClaimProjection(string ClaimType, string ClaimValue, string DisplayName);

public interface IAuthenticatedAccountApiAdapter
{
    Task<Result<AccountProfileProjection, AppError>> CheckTokenAsync(
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

public interface IAccountAccessApiAdapter
{
    Task<bool> IsAdminAsync(Id<AccountReference> accountId, CancellationToken cancellationToken = default);
}

public interface IAccountEloApiAdapter
{
    Task<AccountProfileProjection> PopulateLatestEloAsync(
        AccountProfileProjection account,
        CancellationToken cancellationToken = default);

    Task<Result<int, AppError>> GetUserEloAsync(
        Id<AccountReference> accountId,
        CancellationToken cancellationToken = default);
}

public interface IAccountExternalLoginApiAdapter
{
    Task<Result<Unit, AppError>> LinkGoogleAsync(
        Id<AccountReference> accountId,
        string idToken,
        string? accessToken,
        CancellationToken cancellationToken = default);

    Task<Result<Unit, AppError>> UnlinkGoogleAsync(
        Id<AccountReference> accountId,
        CancellationToken cancellationToken = default);

    Task<Result<IReadOnlyList<ExternalLoginProjection>, AppError>> GetExternalLoginsAsync(
        Id<AccountReference> accountId,
        CancellationToken cancellationToken = default);
}

public interface IAccountTutorialApiAdapter
{
    Task<Result<IReadOnlyList<TutorialProgressProjection>, AppError>> GetActiveTutorialsAsync(
        Id<AccountReference> accountId,
        CancellationToken cancellationToken = default);

    Task<Result<TutorialProgressProjection?, AppError>> GetTutorialProgressAsync(
        Id<AccountReference> accountId,
        TutorialType tutorialType,
        CancellationToken cancellationToken = default);

    Task<Result<Unit, AppError>> CompleteStepAsync(
        Id<AccountReference> accountId,
        TutorialType tutorialType,
        TutorialStep step,
        CancellationToken cancellationToken = default);

    Task<Result<Unit, AppError>> CompleteTutorialAsync(
        Id<AccountReference> accountId,
        TutorialType tutorialType,
        CancellationToken cancellationToken = default);
}

public interface IAdminAccountManagementApiAdapter
{
    Task<Result<Pagination<AdminAccountProjection>, AppError>> GetUsersAsync(
        FilterInput filterInput,
        bool includeDeleted,
        CancellationToken cancellationToken = default);

    Task<Result<AdminAccountProjection, AppError>> GetUserAsync(
        Id<AccountReference> accountId,
        CancellationToken cancellationToken = default);

    Task<Result<Unit, AppError>> UpdateUserAsync(
        Id<AccountReference> targetAccountId,
        Id<AccountReference> administratorAccountId,
        UpdateUserCommand command,
        CancellationToken cancellationToken = default);

    Task<Result<Unit, AppError>> DeleteUserAsync(
        Id<AccountReference> targetAccountId,
        Id<AccountReference> administratorAccountId,
        CancellationToken cancellationToken = default);

    Task<Result<Unit, AppError>> BlockUserAsync(
        Id<AccountReference> targetAccountId,
        Id<AccountReference> administratorAccountId,
        CancellationToken cancellationToken = default);

    Task<Result<Unit, AppError>> UnblockUserAsync(
        Id<AccountReference> targetAccountId,
        CancellationToken cancellationToken = default);
}

public interface IRoleManagementApiAdapter
{
    Task<Result<IReadOnlyList<RoleProjection>, AppError>> GetRolesAsync(CancellationToken cancellationToken = default);

    Task<Result<Pagination<RoleProjection>, AppError>> GetRolesPaginatedAsync(
        FilterInput filterInput,
        CancellationToken cancellationToken = default);

    Task<Result<RoleProjection, AppError>> GetRoleAsync(
        Id<RoleReference> roleId,
        CancellationToken cancellationToken = default);

    Task<Result<RoleProjection, AppError>> CreateRoleAsync(
        string name,
        string? description,
        IReadOnlyCollection<string> permissionClaims,
        CancellationToken cancellationToken = default);

    Task<Result<Unit, AppError>> UpdateRoleAsync(
        Id<RoleReference> roleId,
        string name,
        string? description,
        IReadOnlyCollection<string> permissionClaims,
        CancellationToken cancellationToken = default);

    Task<Result<Unit, AppError>> DeleteRoleAsync(
        Id<RoleReference> roleId,
        CancellationToken cancellationToken = default);

    IReadOnlyList<PermissionClaimProjection> GetAvailablePermissionClaims();

    Task<Result<Unit, AppError>> UpdateUserRolesAsync(
        Id<AccountReference> accountId,
        IReadOnlyCollection<string> roleNames,
        CancellationToken cancellationToken = default);
}
