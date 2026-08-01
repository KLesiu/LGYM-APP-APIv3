using FluentAssertions;
using LgymApi.Application.Identity.Contracts.Sessions;
using LgymApi.Application.Identity.Sessions;
using LgymApi.Application.Repositories;
using LgymApi.Application.Services;
using LgymApi.Domain.Entities;
using LgymApi.Domain.ValueObjects;
using LgymApi.Identity.Contracts;
using LgymApi.Infrastructure.Data;
using LgymApi.Infrastructure.UnitOfWork;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace LgymApi.IntegrationTests;

[TestFixture]
[Category("PostgreSql")]
internal sealed class PostgreSqlIdentitySharedUnitOfWorkTests : PostgreSqlIntegrationTestBase
{
    [Test]
    public async Task LogoutAsync_StagesIdentityAndNotificationsWritesUntilOneSharedCommit()
    {
        var (user, sessionId, installationId) = await SeedSessionAndInstallationAsync();
        var stagedWritesWereInvisible = false;

        await using (var serviceScope = Factory.Services.CreateAsyncScope())
        {
            var database = serviceScope.ServiceProvider.GetRequiredService<AppDbContext>();
            var unitOfWork = new ObservedUnitOfWork(new EfUnitOfWork(database), async () =>
            {
                var state = await ReadStateAsync(sessionId, installationId);
                stagedWritesWereInvisible = state.RevokedAtUtc is null
                    && state.UserId == user.Id
                    && state.SessionId == sessionId;
            });
            var service = new UserSessionTerminationService(
                serviceScope.ServiceProvider.GetRequiredService<IUserSessionStore>(),
                serviceScope.ServiceProvider.GetRequiredService<IAccountSessionDisassociationPort>(),
                unitOfWork);

            var result = await service.LogoutAsync(user, sessionId);

            result.IsSuccess.Should().BeTrue();
            unitOfWork.SaveChangesCalls.Should().Be(1);
            unitOfWork.BeginTransactionCalls.Should().Be(0);
        }

        stagedWritesWereInvisible.Should().BeTrue();
        var persistedState = await ReadStateAsync(sessionId, installationId);
        persistedState.RevokedAtUtc.Should().NotBeNull();
        persistedState.UserId.Should().BeNull();
        persistedState.SessionId.Should().BeNull();
    }

    [Test]
    public async Task LogoutAsync_WhenNotificationsStagingFails_DoesNotPersistTheIdentityWrite()
    {
        var (user, sessionId, installationId) = await SeedSessionAndInstallationAsync();
        var stagingFailure = new InvalidOperationException("Forced notification staging failure.");

        await using (var serviceScope = Factory.Services.CreateAsyncScope())
        {
            var database = serviceScope.ServiceProvider.GetRequiredService<AppDbContext>();
            var unitOfWork = new ObservedUnitOfWork(new EfUnitOfWork(database));
            var service = new UserSessionTerminationService(
                serviceScope.ServiceProvider.GetRequiredService<IUserSessionStore>(),
                new FailingSessionDisassociationPort(stagingFailure),
                unitOfWork);

            var action = () => service.LogoutAsync(user, sessionId);

            await action.Should().ThrowAsync<InvalidOperationException>()
                .Where(exception => ReferenceEquals(exception, stagingFailure));
            unitOfWork.SaveChangesCalls.Should().Be(0);
        }

        var persistedState = await ReadStateAsync(sessionId, installationId);
        persistedState.RevokedAtUtc.Should().BeNull();
        persistedState.UserId.Should().Be(user.Id);
        persistedState.SessionId.Should().Be(sessionId);
    }

    private async Task<(User User, Id<UserSession> SessionId, Id<PushInstallation> InstallationId)> SeedSessionAndInstallationAsync()
    {
        var user = await SeedUserAsync(
            $"shared-uow-user-{Id<User>.New():N}",
            $"shared-uow-{Id<User>.New():N}@example.com");
        var session = new UserSession
        {
            Id = Id<UserSession>.New(),
            UserId = user.Id,
            Jti = Id<UserSession>.New().ToString(),
            ExpiresAtUtc = DateTimeOffset.UtcNow.AddDays(1)
        };
        var installation = new PushInstallation
        {
            Id = Id<PushInstallation>.New(),
            UserId = user.Id,
            SessionId = session.Id,
            InstallationId = $"shared-uow-{Id<PushInstallation>.New():N}",
            Platform = "android",
            FcmToken = "test-token",
            Environment = "test",
            PermissionStatus = "authorized",
            LastSeenAt = DateTimeOffset.UtcNow
        };

        await using var setupScope = Factory.Services.CreateAsyncScope();
        var database = setupScope.ServiceProvider.GetRequiredService<AppDbContext>();
        database.UserSessions.Add(session);
        database.PushInstallations.Add(installation);
        await database.SaveChangesAsync();
        return (user, session.Id, installation.Id);
    }

    private async Task<PersistedSessionState> ReadStateAsync(
        Id<UserSession> sessionId,
        Id<PushInstallation> installationId)
    {
        await using var verificationScope = Factory.Services.CreateAsyncScope();
        var database = verificationScope.ServiceProvider.GetRequiredService<AppDbContext>();
        var revokedAtUtc = await database.UserSessions
            .AsNoTracking()
            .Where(session => session.Id == sessionId)
            .Select(session => session.RevokedAtUtc)
            .SingleAsync();
        var installation = await database.PushInstallations
            .AsNoTracking()
            .SingleAsync(candidate => candidate.Id == installationId);
        return new PersistedSessionState(revokedAtUtc, installation.UserId, installation.SessionId);
    }

    private sealed record PersistedSessionState(
        DateTimeOffset? RevokedAtUtc,
        Id<User>? UserId,
        Id<UserSession>? SessionId);

    private sealed class FailingSessionDisassociationPort(Exception exception) : IAccountSessionDisassociationPort
    {
        public Task StageDisassociateAsync(
            Id<AccountReference> accountId,
            Id<AccountSessionReference> sessionId,
            CancellationToken cancellationToken = default)
            => Task.FromException(exception);
    }
}
