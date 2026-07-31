using FluentAssertions;
using LgymApi.Application.BuildingBlocks.Errors;
using LgymApi.Application.Identity.Errors;
using LgymApi.Application.Identity.Contracts.Sessions;
using LgymApi.Application.Identity.Sessions;
using LgymApi.Application.Repositories;
using LgymApi.Application.Services;
using LgymApi.Domain.Entities;
using LgymApi.Domain.ValueObjects;
using LgymApi.Identity.Contracts;
using LgymApi.Infrastructure.Data;
using LgymApi.Infrastructure.Services;
using LgymApi.Infrastructure.UnitOfWork;
using Microsoft.EntityFrameworkCore;
using NSubstitute;

namespace LgymApi.UnitTests;

[TestFixture]
public sealed class UserSessionTerminationServiceTests
{
    [Test]
    public async Task LogoutAsync_RevokesDisassociatesAndCommitsExactlyOnce_WhenSessionIsPresent()
    {
        var userSessionStore = new RecordingUserSessionStore();
        var unitOfWork = Substitute.For<IUnitOfWork>();
        var sessionId = Id<UserSession>.New();
        var currentUser = new User { Id = Id<User>.New(), Name = "user", Email = "user@example.com" };
        using var cancellationSource = new CancellationTokenSource();
        var cancellationToken = cancellationSource.Token;
        var operations = new List<string>();
        var disassociationPort = Substitute.For<IAccountSessionDisassociationPort>();
        var disassociatedAccountId = Id<AccountReference>.Empty;
        var disassociatedSessionId = Id<AccountSessionReference>.Empty;
        var disassociationToken = default(CancellationToken);

        userSessionStore.OnRevoke = () => operations.Add("revoke");
        unitOfWork.SaveChangesAsync(cancellationToken).Returns(_ =>
        {
            operations.Add("commit");
            return Task.FromResult(1);
        });
        disassociationPort.StageDisassociateAsync(
                currentUser.Id.Rebind<AccountReference>(),
                sessionId.Rebind<AccountSessionReference>(),
                cancellationToken)
            .Returns(_ =>
            {
                operations.Add("disassociate");
                disassociatedAccountId = currentUser.Id.Rebind<AccountReference>();
                disassociatedSessionId = sessionId.Rebind<AccountSessionReference>();
                disassociationToken = cancellationToken;
                return Task.CompletedTask;
            });
        var service = new UserSessionTerminationService(
            userSessionStore,
            disassociationPort,
            unitOfWork);

        var result = await service.LogoutAsync(currentUser, sessionId, cancellationToken);

        result.IsSuccess.Should().BeTrue();
        operations.Should().Equal("revoke", "disassociate", "commit");
        disassociatedAccountId.Should().Be(currentUser.Id.Rebind<AccountReference>());
        disassociatedSessionId.Should().Be(sessionId.Rebind<AccountSessionReference>());
        disassociationToken.Should().Be(cancellationToken);
        userSessionStore.RevokeCount.Should().Be(1);
        await disassociationPort.Received(1).StageDisassociateAsync(
            currentUser.Id.Rebind<AccountReference>(),
            sessionId.Rebind<AccountSessionReference>(),
            cancellationToken);
        await unitOfWork.Received(1).SaveChangesAsync(cancellationToken);
    }

    [Test]
    public async Task LogoutAsync_ReturnsUserNotFoundWithoutSideEffects_WhenCurrentUserIsMissing()
    {
        var userSessionStore = new RecordingUserSessionStore();
        var unitOfWork = Substitute.For<IUnitOfWork>();
        var disassociationPort = Substitute.For<IAccountSessionDisassociationPort>();
        var service = new UserSessionTerminationService(
            userSessionStore,
            disassociationPort,
            unitOfWork);

        var result = await service.LogoutAsync(null, Id<UserSession>.New());

        result.IsFailure.Should().BeTrue();
        result.Error.Should().BeOfType<UserNotFoundError>();
        userSessionStore.RevokeCount.Should().Be(0);
        await disassociationPort.DidNotReceive().StageDisassociateAsync(
            Arg.Any<Id<AccountReference>>(),
            Arg.Any<Id<AccountSessionReference>>(),
            Arg.Any<CancellationToken>());
        await unitOfWork.DidNotReceive().SaveChangesAsync(Arg.Any<CancellationToken>());
    }

    [Test]
    public async Task LogoutAsync_SucceedsWithoutSideEffects_WhenSessionIsMissing()
    {
        var userSessionStore = new RecordingUserSessionStore();
        var unitOfWork = Substitute.For<IUnitOfWork>();
        var disassociationPort = Substitute.For<IAccountSessionDisassociationPort>();
        var service = new UserSessionTerminationService(
            userSessionStore,
            disassociationPort,
            unitOfWork);
        var currentUser = new User { Id = Id<User>.New(), Name = "user", Email = "user@example.com" };

        var result = await service.LogoutAsync(currentUser, null);

        result.IsSuccess.Should().BeTrue();
        userSessionStore.RevokeCount.Should().Be(0);
        await disassociationPort.DidNotReceive().StageDisassociateAsync(
            Arg.Any<Id<AccountReference>>(),
            Arg.Any<Id<AccountSessionReference>>(),
            Arg.Any<CancellationToken>());
        await unitOfWork.DidNotReceive().SaveChangesAsync(Arg.Any<CancellationToken>());
    }

    private sealed class RecordingUserSessionStore : IUserSessionStore
    {
        public int RevokeCount { get; private set; }
        public Action? OnRevoke { get; set; }
        public Task<UserSession> CreateSessionAsync(Id<User> userId, DateTimeOffset expiresAtUtc, CancellationToken cancellationToken) => throw new NotSupportedException();
        public Task<bool> ValidateSessionAsync(Id<UserSession> sessionId, CancellationToken cancellationToken) => throw new NotSupportedException();
        public Task RevokeSessionAsync(Id<UserSession> sessionId, CancellationToken cancellationToken) { RevokeCount++; OnRevoke?.Invoke(); return Task.CompletedTask; }
        public Task RevokeAllUserSessionsAsync(Id<User> userId, CancellationToken cancellationToken) => throw new NotSupportedException();
    }

    [Test]
    public async Task LogoutAsync_WhenDisassociationStagingFails_LeavesSessionMutationUncommitted()
    {
        var databaseName = $"session-disassociation-failure-{Id<UserSessionTerminationServiceTests>.New():N}";
        var options = new DbContextOptionsBuilder<AppDbContext>()
            .UseInMemoryDatabase(databaseName)
            .Options;
        var userId = Id<User>.New();
        var sessionId = Id<UserSession>.New();

        await using (var setupContext = new AppDbContext(options))
        {
            await setupContext.Database.EnsureCreatedAsync();
            setupContext.Users.Add(new User
            {
                Id = userId,
                Name = "session-failure",
                Email = new Email("session-failure@example.com"),
                ProfileRank = "Rookie"
            });
            setupContext.UserSessions.Add(new UserSession
            {
                Id = sessionId,
                UserId = userId,
                Jti = Id<UserSession>.New().ToString(),
                ExpiresAtUtc = DateTimeOffset.UtcNow.AddDays(1)
            });
            await setupContext.SaveChangesAsync();
        }

        await using (var mutationContext = new AppDbContext(options))
        {
            var disassociationPort = Substitute.For<IAccountSessionDisassociationPort>();
            var stagingFailure = new InvalidOperationException("Push disassociation staging failed.");
            disassociationPort.StageDisassociateAsync(
                    userId.Rebind<AccountReference>(),
                    sessionId.Rebind<AccountSessionReference>(),
                    CancellationToken.None)
                .Returns(Task.FromException(stagingFailure));
            var service = new UserSessionTerminationService(
                new UserSessionStore(mutationContext),
                disassociationPort,
                new EfUnitOfWork(mutationContext));

            var exception = Assert.ThrowsAsync<InvalidOperationException>(async () =>
                await service.LogoutAsync(
                    new User { Id = userId, Name = "session-failure", Email = "session-failure@example.com" },
                    sessionId,
                    CancellationToken.None));

            exception.Should().BeSameAs(stagingFailure);
        }

        await using var verificationContext = new AppDbContext(options);
        var persistedSession = await verificationContext.UserSessions.SingleAsync(session => session.Id == sessionId);
        persistedSession.RevokedAtUtc.Should().BeNull();
    }
}
