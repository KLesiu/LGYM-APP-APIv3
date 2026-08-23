using LgymApi.Domain.ValueObjects;

namespace LgymApi.Identity.Contracts.Accounts;

public enum AuthenticatedAccountResolutionStatus
{
    Active,
    SessionInvalid,
    AccountNotFound,
    AccountDeleted,
    AccountBlocked
}

public sealed record AccountAccessFacts(
    Id<AccountReference> Id,
    bool IsDeleted,
    bool IsBlocked,
    IReadOnlyList<string> Roles,
    IReadOnlyList<string> PermissionClaims,
    DateTimeOffset? AdultConfirmedAt = null);

public sealed record AuthenticatedAccountContext(
    Id<AccountReference> Id,
    Id<AccountSessionReference>? SessionId,
    IReadOnlyList<string> Roles,
    IReadOnlyList<string> PermissionClaims,
    bool IsBlocked,
    bool IsDeleted,
    DateTimeOffset? AdultConfirmedAt = null);

public sealed record AuthenticatedAccountResolution(
    AuthenticatedAccountResolutionStatus Status,
    AuthenticatedAccountContext? Context);

public interface IAccountAccessReader
{
    Task<AccountAccessFacts?> GetByIdAsync(
        Id<AccountReference> accountId,
        CancellationToken cancellationToken = default);
}

public interface IAccountSessionValidator
{
    Task<bool> IsValidAsync(
        Id<AccountSessionReference> sessionId,
        CancellationToken cancellationToken = default);

    Task<bool> IsValidAsync(
        Id<AccountReference> accountId,
        Id<AccountSessionReference> sessionId,
        CancellationToken cancellationToken = default) =>
        IsValidAsync(sessionId, cancellationToken);
}

public interface IAuthenticatedAccountContextResolver
{
    Task<AuthenticatedAccountResolution> ResolveAsync(
        Id<AccountReference> accountId,
        Id<AccountSessionReference> sessionId,
        CancellationToken cancellationToken = default);
}
