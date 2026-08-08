using LgymApi.Application.Services;
using LgymApi.Domain.Entities;
using LgymApi.Domain.ValueObjects;
using LgymApi.Identity.Persistence;
using Microsoft.EntityFrameworkCore;

namespace LgymApi.Infrastructure.Services;

internal sealed class UserSessionStore : IUserSessionStore
{
    private readonly IIdentityPersistenceContext _dbContext;

    public UserSessionStore(IIdentityPersistenceContext dbContext)
    {
        _dbContext = dbContext;
    }

    public async Task<UserSession> CreateSessionAsync(Id<User> userId, DateTimeOffset expiresAtUtc, CancellationToken ct)
    {
        var session = new UserSession
        {
            Id = Id<UserSession>.New(),
            UserId = userId,
            Jti = Id<UserSession>.New().ToString(),
            ExpiresAtUtc = expiresAtUtc
        };

        await _dbContext.UserSessions.AddAsync(session, ct);
        return session;
    }

    public Task<bool> ValidateSessionAsync(Id<UserSession> sessionId, CancellationToken ct)
        => ValidateSessionAsync(default, sessionId, ct);

    public Task<bool> ValidateSessionAsync(Id<User> userId, Id<UserSession> sessionId, CancellationToken ct)
    {
        var now = DateTimeOffset.UtcNow;

        return _dbContext.UserSessions
            .AsNoTracking()
            .AnyAsync(
                session => session.Id == sessionId
                    && (userId == default || session.UserId == userId)
                    && session.RevokedAtUtc == null
                    && session.ExpiresAtUtc > now
                    && !session.IsDeleted,
                ct);
    }

    public async Task RevokeSessionAsync(Id<UserSession> sessionId, CancellationToken ct)
    {
        var session = await _dbContext.UserSessions
            .FirstOrDefaultAsync(s => s.Id == sessionId && !s.IsDeleted, ct);

        if (session is null)
        {
            return;
        }

        session.RevokedAtUtc = DateTimeOffset.UtcNow;
    }

    public async Task RevokeAllUserSessionsAsync(Id<User> userId, CancellationToken ct)
    {
        var now = DateTimeOffset.UtcNow;

        var sessions = await _dbContext.UserSessions
            .Where(session => session.UserId == userId
                && session.RevokedAtUtc == null
                && session.ExpiresAtUtc > now
                && !session.IsDeleted)
            .ToListAsync(ct);

        foreach (var session in sessions)
        {
            session.RevokedAtUtc = now;
        }
    }
}
