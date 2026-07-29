using FluentAssertions;
using LgymApi.Application.Identity.Contracts.Ranking;
using LgymApi.Application.WorkoutProgress.Persistence;
using LgymApi.Application.WorkoutProgress.Ranking;
using LgymApi.Domain.Entities;
using LgymApi.Domain.ValueObjects;
using LgymApi.Identity.Contracts;
using NSubstitute;

namespace LgymApi.UnitTests;

[TestFixture]
public sealed class WorkoutProgressRankingReadServiceTests
{
    [Test]
    public async Task GetUsersRankingAsync_ProjectsEligibleProfilesWithLatestEloDefaultAndDescendingOrder()
    {
        var higherEloUserId = Id<AccountReference>.New();
        var defaultEloUserId = Id<AccountReference>.New();
        var profiles = Substitute.For<IRankingAccountProfileReadService>();
        var eloRegistry = Substitute.For<IWorkoutEloPersistence>();
        profiles.GetRankingEligibleAccountProfilesAsync(Arg.Any<CancellationToken>()).Returns(
        [
            new RankingAccountProfile(defaultEloUserId, "default", null, "Junior 1"),
            new RankingAccountProfile(higherEloUserId, "higher", "avatar", "Senior 1")
        ]);
        eloRegistry.GetLatestEloAsync(defaultEloUserId, Arg.Any<CancellationToken>()).Returns((int?)null);
        eloRegistry.GetLatestEloAsync(higherEloUserId, Arg.Any<CancellationToken>()).Returns(1450);
        var service = new WorkoutProgressRankingReadService(profiles, eloRegistry);

        var result = await service.GetUsersRankingAsync();

        result.IsSuccess.Should().BeTrue();
        result.Value.Select(entry => entry.Name).Should().Equal("higher", "default");
        result.Value.Select(entry => entry.Avatar).Should().Equal("avatar", null);
        result.Value.Select(entry => entry.Elo).Should().Equal(1450, 1000);
        result.Value.Select(entry => entry.ProfileRank).Should().Equal("Senior 1", "Junior 1");
        await eloRegistry.Received(1).GetLatestEloAsync(defaultEloUserId, Arg.Any<CancellationToken>());
        await eloRegistry.Received(1).GetLatestEloAsync(higherEloUserId, Arg.Any<CancellationToken>());
    }

    [Test]
    public async Task GetUsersRankingAsync_ReturnsNotFound_WhenNoEligibleProfilesExist()
    {
        var profiles = Substitute.For<IRankingAccountProfileReadService>();
        profiles.GetRankingEligibleAccountProfilesAsync(Arg.Any<CancellationToken>()).Returns([]);
        var eloRegistry = Substitute.For<IWorkoutEloPersistence>();
        var service = new WorkoutProgressRankingReadService(profiles, eloRegistry);

        var result = await service.GetUsersRankingAsync();

        result.IsFailure.Should().BeTrue();
        await eloRegistry.DidNotReceive().GetLatestEloAsync(Arg.Any<Id<AccountReference>>(), Arg.Any<CancellationToken>());
    }
}
