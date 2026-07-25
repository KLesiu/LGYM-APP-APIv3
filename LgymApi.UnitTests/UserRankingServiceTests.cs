using FluentAssertions;
using LgymApi.Application.BuildingBlocks.Errors;
using LgymApi.Application.Identity.Errors;
using LgymApi.Application.Identity.Ranking;
using LgymApi.Application.Repositories;
using LgymApi.Application.Services;
using LgymApi.Domain.Entities;
using NSubstitute;

namespace LgymApi.UnitTests;

[TestFixture]
public sealed class UserRankingServiceTests
{
    [Test]
    public async Task ChangeVisibilityInRankingAsync_UpdatesIdentityAccountAndCommits()
    {
        var repository = Substitute.For<IUserRepository>();
        var unitOfWork = Substitute.For<IUnitOfWork>();
        repository.UpdateAsync(Arg.Any<User>(), Arg.Any<CancellationToken>()).Returns(Task.CompletedTask);
        unitOfWork.SaveChangesAsync(Arg.Any<CancellationToken>()).Returns(1);
        var user = new User { IsVisibleInRanking = true };
        var service = new UserRankingService(repository, unitOfWork);

        var result = await service.ChangeVisibilityInRankingAsync(user, false);

        result.IsSuccess.Should().BeTrue();
        user.IsVisibleInRanking.Should().BeFalse();
        await repository.Received(1).UpdateAsync(user, Arg.Any<CancellationToken>());
        await unitOfWork.Received(1).SaveChangesAsync(Arg.Any<CancellationToken>());
    }

    [Test]
    public async Task ChangeVisibilityInRankingAsync_ReturnsInvalidUserErrorWithoutCommit_WhenCurrentUserIsMissing()
    {
        var unitOfWork = Substitute.For<IUnitOfWork>();
        var service = new UserRankingService(Substitute.For<IUserRepository>(), unitOfWork);

        var result = await service.ChangeVisibilityInRankingAsync(null, true);

        result.IsFailure.Should().BeTrue();
        result.Error.Should().BeOfType<InvalidUserError>();
        await unitOfWork.DidNotReceive().SaveChangesAsync(Arg.Any<CancellationToken>());
    }
}
