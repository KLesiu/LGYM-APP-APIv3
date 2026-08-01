using FluentAssertions;
using LgymApi.Application.Coaching.Adapters;
using LgymApi.Application.Coaching.Persistence;
using LgymApi.Domain.Security;
using LgymApi.Domain.ValueObjects;
using LgymApi.Identity.Contracts;
using LgymApi.Identity.Contracts.Accounts;
using NSubstitute;

namespace LgymApi.UnitTests;

[TestFixture]
public sealed class MeasurementsRelationshipAccessAdapterTests
{
    [Test]
    public async Task TrainerWithActiveLink_ShouldBeAllowed()
    {
        var trainerId = Id<AccountReference>.New();
        var traineeId = Id<AccountReference>.New();
        var accounts = Substitute.For<IAccountAccessReader>();
        var links = Substitute.For<ICoachingActiveLinkPersistence>();
        accounts.GetByIdAsync(trainerId, Arg.Any<CancellationToken>()).Returns(new AccountAccessFacts(trainerId, false, false, [AuthConstants.Roles.Trainer], []));
        links.HasActiveRelationshipAsync(trainerId, traineeId, Arg.Any<CancellationToken>()).Returns(true);

        var result = await new MeasurementsRelationshipAccessAdapter(accounts, links).HasActiveRelationshipAsync(trainerId, traineeId);

        result.Should().BeTrue();
    }

    [Test]
    public async Task NonTrainer_ShouldNotQueryLinks()
    {
        var trainerId = Id<AccountReference>.New();
        var links = Substitute.For<ICoachingActiveLinkPersistence>();
        var result = await new MeasurementsRelationshipAccessAdapter(Substitute.For<IAccountAccessReader>(), links).HasActiveRelationshipAsync(trainerId, Id<AccountReference>.New());
        result.Should().BeFalse();
        await links.DidNotReceiveWithAnyArgs().HasActiveRelationshipAsync(default, default);
    }
}
