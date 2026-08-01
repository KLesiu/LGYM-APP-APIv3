using LgymApi.Application.Services;
using LgymApi.Domain.Entities;
using LgymApi.Domain.ValueObjects;

namespace LgymApi.TestUtils.Fakes;

internal sealed class FakeUserSessionStore : IUserSessionStore
{
    private readonly Dictionary<Id<UserSession>, UserSession> sessions = new();

    public List<(string Method, object? Argument, CancellationToken CancellationToken)> Calls { get; } = [];
    public List<Id<UserSession>> RevokedSessionIds { get; } = [];
    public List<Id<User>> RevokedAllUserIds { get; } = [];
    public Func<Id<User>, DateTimeOffset, CancellationToken, Task<UserSession>>? CreateSessionHandler { get; set; }
    public Func<Id<UserSession>, CancellationToken, Task<bool>>? ValidateSessionHandler { get; set; }
    public Func<Id<UserSession>, CancellationToken, Task>? RevokeSessionHandler { get; set; }
    public Func<Id<User>, CancellationToken, Task>? RevokeAllUserSessionsHandler { get; set; }

    public Task<UserSession> CreateSessionAsync(Id<User> userId, DateTimeOffset expiresAtUtc, CancellationToken cancellationToken)
    {
        Calls.Add((nameof(CreateSessionAsync), (userId, expiresAtUtc), cancellationToken));
        if (CreateSessionHandler is not null)
        {
            return CreateSessionHandler(userId, expiresAtUtc, cancellationToken);
        }

        var session = new UserSession
        {
            Id = Id<UserSession>.New(),
            UserId = userId,
            Jti = Id<UserSession>.New().ToString(),
            ExpiresAtUtc = expiresAtUtc
        };

        sessions[session.Id] = session;
        return Task.FromResult(session);
    }

    public Task<bool> ValidateSessionAsync(Id<UserSession> sessionId, CancellationToken cancellationToken)
    {
        Calls.Add((nameof(ValidateSessionAsync), sessionId, cancellationToken));
        if (ValidateSessionHandler is not null)
        {
            return ValidateSessionHandler(sessionId, cancellationToken);
        }

        var isValid = sessions.TryGetValue(sessionId, out var session)
            && session.RevokedAtUtc == null
            && session.ExpiresAtUtc > DateTimeOffset.UtcNow
            && !session.IsDeleted;

        return Task.FromResult(isValid);
    }

    public Task RevokeSessionAsync(Id<UserSession> sessionId, CancellationToken cancellationToken)
    {
        Calls.Add((nameof(RevokeSessionAsync), sessionId, cancellationToken));
        if (RevokeSessionHandler is not null)
        {
            return RevokeSessionHandler(sessionId, cancellationToken);
        }

        RevokedSessionIds.Add(sessionId);
        if (sessions.TryGetValue(sessionId, out var session))
        {
            session.RevokedAtUtc = DateTimeOffset.UtcNow;
        }

        return Task.CompletedTask;
    }

    public Task RevokeAllUserSessionsAsync(Id<User> userId, CancellationToken cancellationToken)
    {
        Calls.Add((nameof(RevokeAllUserSessionsAsync), userId, cancellationToken));
        if (RevokeAllUserSessionsHandler is not null)
        {
            return RevokeAllUserSessionsHandler(userId, cancellationToken);
        }

        RevokedAllUserIds.Add(userId);
        foreach (var session in sessions.Values.Where(session => session.UserId == userId))
        {
            session.RevokedAtUtc = DateTimeOffset.UtcNow;
        }

        return Task.CompletedTask;
    }
}
