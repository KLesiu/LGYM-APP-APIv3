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
