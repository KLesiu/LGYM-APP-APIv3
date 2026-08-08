using LgymApi.Domain.Entities;
using LgymApi.Domain.ValueObjects;

namespace LgymApi.Application.Services;

internal interface IUserSessionStore
{
    Task<UserSession> CreateSessionAsync(Id<User> userId, DateTimeOffset expiresAtUtc, CancellationToken ct);
    Task<bool> ValidateSessionAsync(Id<UserSession> sessionId, CancellationToken ct);
    Task<bool> ValidateSessionAsync(Id<User> userId, Id<UserSession> sessionId, CancellationToken ct) =>
        ValidateSessionAsync(sessionId, ct);
    Task RevokeSessionAsync(Id<UserSession> sessionId, CancellationToken ct);
    Task RevokeAllUserSessionsAsync(Id<User> userId, CancellationToken ct);
}
