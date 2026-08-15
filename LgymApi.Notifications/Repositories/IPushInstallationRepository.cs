using LgymApi.Domain.Entities;
using LgymApi.Domain.ValueObjects;
using LgymApi.Identity.Contracts;

namespace LgymApi.Application.Notifications.Repositories;

internal interface IPushInstallationRepository
{
    Task<PushInstallation?> FindByIdAsync(Id<PushInstallation> id, CancellationToken cancellationToken = default);
    Task<PushInstallation?> FindByInstallationIdAsync(string installationId, CancellationToken cancellationToken = default);
    Task<PushInstallation?> FindBoundToUserOrSessionAsync(string installationId, Id<User> userId, Id<UserSession> sessionId, CancellationToken cancellationToken = default);
    Task<List<PushInstallation>> GetActiveByUserIdAsync(Id<User> userId, CancellationToken cancellationToken = default);
    Task<List<PushInstallation>> GetBySessionIdAsync(Id<UserSession> sessionId, CancellationToken cancellationToken = default);
    Task<List<PushInstallation>> GetStaleActiveAsync(DateTimeOffset lastSeenBefore, int limit, CancellationToken cancellationToken = default);
    Task AddAsync(PushInstallation installation, CancellationToken cancellationToken = default);
    Task UpdateAsync(PushInstallation installation, CancellationToken cancellationToken = default);
    Task UpsertForUserSessionAsync(PushInstallationRegistration registration, CancellationToken cancellationToken = default);
    Task<bool> DisableBoundForUserOrSessionAsync(string installationId, Id<User> userId, Id<UserSession> sessionId, DateTimeOffset disabledAt, string disabledReason, CancellationToken cancellationToken = default);
    Task<bool> DisassociateBoundForUserOrSessionAsync(string installationId, Id<User> userId, Id<UserSession> sessionId, DateTimeOffset lastSeenAt, CancellationToken cancellationToken = default);
    Task DisassociateForSessionAsync(Id<AccountSessionReference> sessionId, DateTimeOffset lastSeenAt, CancellationToken cancellationToken = default);
    Task RemoveForAccountAsync(Id<User> userId, CancellationToken cancellationToken = default);

    /// <summary>
    /// Returns no more than the positive candidate limit of installations, including soft-deleted rows, with a non-null DisabledAt strictly before the caller-computed UTC cutoff, ordered oldest first.
    /// Active and session-disassociated-only installations are excluded.
    /// </summary>
    Task<IReadOnlyList<PushInstallation>> GetRetentionCandidatesDisabledBeforeAsync(
        DateTimeOffset cutoff,
        int candidateLimit,
        CancellationToken cancellationToken = default)
        => throw new NotSupportedException();

    /// <summary>
    /// Stages deletion of the selected disabled installations; the caller owns the unit-of-work commit.
    /// </summary>
    void RemoveRange(IEnumerable<PushInstallation> installations)
        => throw new NotSupportedException();
}

internal sealed record PushInstallationRegistration(
    string InstallationId,
    string Platform,
    string FcmToken,
    string? AppVersion,
    string Environment,
    string? PermissionStatus,
    Id<User> UserId,
    Id<UserSession> SessionId,
    DateTimeOffset LastSeenAt);
