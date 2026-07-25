using FluentAssertions;
using LgymApi.Application.BuildingBlocks.Errors;
using LgymApi.Application.Identity.Errors;
using LgymApi.Application.Identity.Sessions;
using LgymApi.Application.Repositories;
using LgymApi.Application.Services;
using LgymApi.Domain.Entities;
using LgymApi.Domain.ValueObjects;
using NSubstitute;

namespace LgymApi.UnitTests;

[TestFixture]
public sealed class UserSessionTerminationServiceTests
{
    [Test]
    public async Task LogoutAsync_RevokesDisassociatesAndCommitsExactlyOnce_WhenSessionIsPresent()
    {
        var userSessionStore = Substitute.For<IUserSessionStore>();
        var unitOfWork = Substitute.For<IUnitOfWork>();
        var sessionId = Id<UserSession>.New();
        var currentUser = new User { Id = Id<User>.New(), Name = "user", Email = "user@example.com" };
        using var cancellationSource = new CancellationTokenSource();
        var cancellationToken = cancellationSource.Token;
        var operations = new List<string>();
        var disassociatedSessionId = Id<UserSession>.Empty;
        var disassociationToken = default(CancellationToken);

        userSessionStore.RevokeSessionAsync(sessionId, cancellationToken).Returns(_ =>
        {
            operations.Add("revoke");
            return Task.CompletedTask;
        });
        unitOfWork.SaveChangesAsync(cancellationToken).Returns(_ =>
        {
            operations.Add("commit");
            return Task.FromResult(1);
        });
        var service = new UserSessionTerminationService(new UserSessionTerminationServiceDependencies(
            userSessionStore,
            (id, token) =>
            {
                operations.Add("disassociate");
                disassociatedSessionId = id;
                disassociationToken = token;
                return Task.CompletedTask;
            },
            unitOfWork));

        var result = await service.LogoutAsync(currentUser, sessionId, cancellationToken);

        result.IsSuccess.Should().BeTrue();
        operations.Should().Equal("revoke", "disassociate", "commit");
        disassociatedSessionId.Should().Be(sessionId);
        disassociationToken.Should().Be(cancellationToken);
        await userSessionStore.Received(1).RevokeSessionAsync(sessionId, cancellationToken);
        await unitOfWork.Received(1).SaveChangesAsync(cancellationToken);
    }

    [Test]
    public async Task LogoutAsync_ReturnsUserNotFoundWithoutSideEffects_WhenCurrentUserIsMissing()
    {
        var userSessionStore = Substitute.For<IUserSessionStore>();
        var unitOfWork = Substitute.For<IUnitOfWork>();
        var disassociationCalls = 0;
        var service = new UserSessionTerminationService(new UserSessionTerminationServiceDependencies(
            userSessionStore,
            (_, _) =>
            {
                disassociationCalls++;
                return Task.CompletedTask;
            },
            unitOfWork));

        var result = await service.LogoutAsync(null, Id<UserSession>.New());

        result.IsFailure.Should().BeTrue();
        result.Error.Should().BeOfType<UserNotFoundError>();
        disassociationCalls.Should().Be(0);
        await userSessionStore.DidNotReceive().RevokeSessionAsync(Arg.Any<Id<UserSession>>(), Arg.Any<CancellationToken>());
        await unitOfWork.DidNotReceive().SaveChangesAsync(Arg.Any<CancellationToken>());
    }

    [Test]
    public async Task LogoutAsync_SucceedsWithoutSideEffects_WhenSessionIsMissing()
    {
        var userSessionStore = Substitute.For<IUserSessionStore>();
        var unitOfWork = Substitute.For<IUnitOfWork>();
        var disassociationCalls = 0;
        var service = new UserSessionTerminationService(new UserSessionTerminationServiceDependencies(
            userSessionStore,
            (_, _) =>
            {
                disassociationCalls++;
                return Task.CompletedTask;
            },
            unitOfWork));
        var currentUser = new User { Id = Id<User>.New(), Name = "user", Email = "user@example.com" };

        var result = await service.LogoutAsync(currentUser, null);

        result.IsSuccess.Should().BeTrue();
        disassociationCalls.Should().Be(0);
        await userSessionStore.DidNotReceive().RevokeSessionAsync(Arg.Any<Id<UserSession>>(), Arg.Any<CancellationToken>());
        await unitOfWork.DidNotReceive().SaveChangesAsync(Arg.Any<CancellationToken>());
    }
}
