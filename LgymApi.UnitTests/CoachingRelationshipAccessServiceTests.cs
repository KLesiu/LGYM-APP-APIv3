using FluentAssertions;
using LgymApi.Application.Coaching;
using LgymApi.Application.Coaching.Access;
using LgymApi.Application.Coaching.Contracts.Access;
using LgymApi.Application.Coaching.Persistence;
using LgymApi.Application.Identity.Contracts.Access;
using LgymApi.Identity.Contracts.Accounts;
using LgymApi.Domain.Entities;
using LgymApi.Domain.ValueObjects;
using Microsoft.Extensions.DependencyInjection;
using NSubstitute;

namespace LgymApi.UnitTests;

[TestFixture]
public sealed class CoachingRelationshipAccessServiceTests
{
    private IUserAccessReadService _userAccess = null!;
    private ICoachingActiveLinkPersistence _activeLinks = null!;
    private CoachingRelationshipAccessService _service = null!;

    [SetUp]
    public void SetUp()
    {
        _userAccess = Substitute.For<IUserAccessReadService>();
        _activeLinks = Substitute.For<ICoachingActiveLinkPersistence>();
        _service = new CoachingRelationshipAccessService(_userAccess, _activeLinks, Substitute.For<IAccountAccessReader>());
    }

    [Test]
    public async Task GetAccessDecisionAsync_WhenTrainerHasActiveLink_ReturnsTrainerWithRelationship()
    {
        var trainerId = Id<User>.New();
        var traineeId = Id<User>.New();
        _userAccess.IsTrainerAsync(trainerId, CancellationToken.None).Returns(true);
        _activeLinks.FindByTrainerAndTraineeAsync(trainerId, traineeId, CancellationToken.None)
            .Returns(new CoachingActiveLinkFact(
                Id<TrainerTraineeLink>.New(),
                trainerId,
                traineeId,
                DateTimeOffset.UtcNow,
                DateTimeOffset.UtcNow));

        var result = await _service.GetAccessDecisionAsync(trainerId, traineeId);

        result.Should().Be(new CoachingRelationshipAccessDecision(true, true));
    }

    [Test]
    public async Task GetAccessDecisionAsync_WhenTrainerHasNoActiveLink_ReturnsTrainerWithoutRelationship()
    {
        var trainerId = Id<User>.New();
        var traineeId = Id<User>.New();
        _userAccess.IsTrainerAsync(trainerId, CancellationToken.None).Returns(true);
        _activeLinks.FindByTrainerAndTraineeAsync(trainerId, traineeId, CancellationToken.None)
            .Returns((CoachingActiveLinkFact?)null);

        var result = await _service.GetAccessDecisionAsync(trainerId, traineeId);

        result.Should().Be(new CoachingRelationshipAccessDecision(true, false));
    }

    [Test]
    public async Task GetAccessDecisionAsync_WhenUserIsNotTrainer_ReturnsNonTrainerWithoutQueryingLink()
    {
        var trainerId = Id<User>.New();
        var traineeId = Id<User>.New();
        _userAccess.IsTrainerAsync(trainerId, CancellationToken.None).Returns(false);

        var result = await _service.GetAccessDecisionAsync(trainerId, traineeId);

        result.Should().Be(new CoachingRelationshipAccessDecision(false, false));
        await _activeLinks.DidNotReceive().FindByTrainerAndTraineeAsync(
            Arg.Any<Id<User>>(),
            Arg.Any<Id<User>>(),
            Arg.Any<CancellationToken>());
    }

    [Test]
    public async Task GetAccessDecisionAsync_WhenTrainerIdIsEmpty_ReturnsNoAccessWithoutQueries()
    {
        var result = await _service.GetAccessDecisionAsync(Id<User>.Empty, Id<User>.New());

        result.Should().Be(new CoachingRelationshipAccessDecision(false, false));
        await _userAccess.DidNotReceive().IsTrainerAsync(Arg.Any<Id<User>>(), Arg.Any<CancellationToken>());
        await _activeLinks.DidNotReceive().FindByTrainerAndTraineeAsync(
            Arg.Any<Id<User>>(),
            Arg.Any<Id<User>>(),
            Arg.Any<CancellationToken>());
    }

    [Test]
    public async Task GetAccessDecisionAsync_WhenTraineeIdIsEmpty_ReturnsTrainerWithoutQueryingLink()
    {
        var trainerId = Id<User>.New();
        _userAccess.IsTrainerAsync(trainerId, CancellationToken.None).Returns(true);

        var result = await _service.GetAccessDecisionAsync(trainerId, Id<User>.Empty);

        result.Should().Be(new CoachingRelationshipAccessDecision(true, false));
        await _activeLinks.DidNotReceive().FindByTrainerAndTraineeAsync(
            Arg.Any<Id<User>>(),
            Arg.Any<Id<User>>(),
            Arg.Any<CancellationToken>());
    }

    [Test]
    public async Task GetAccessDecisionAsync_ForwardsCancellationTokenToRoleAndLinkReads()
    {
        var trainerId = Id<User>.New();
        var traineeId = Id<User>.New();
        using var cancellationSource = new CancellationTokenSource();
        var cancellationToken = cancellationSource.Token;
        _userAccess.IsTrainerAsync(trainerId, cancellationToken).Returns(true);
        _activeLinks.FindByTrainerAndTraineeAsync(trainerId, traineeId, cancellationToken)
            .Returns((CoachingActiveLinkFact?)null);

        await _service.GetAccessDecisionAsync(trainerId, traineeId, cancellationToken);

        await _userAccess.Received(1).IsTrainerAsync(trainerId, cancellationToken);
        await _activeLinks.Received(1).FindByTrainerAndTraineeAsync(trainerId, traineeId, cancellationToken);
    }

    [Test]
    public void AddCoachingModule_RegistersRelationshipAccessServiceExactlyOnceAndResolvesIt()
    {
        var services = new ServiceCollection();
        services.AddScoped(_ => Substitute.For<IUserAccessReadService>());
        services.AddScoped(_ => Substitute.For<ICoachingActiveLinkPersistence>());
        services.AddScoped(_ => Substitute.For<IAccountAccessReader>());
        services.AddCoachingModule();

        services.Count(descriptor => descriptor.ServiceType == typeof(ICoachingRelationshipAccessService))
            .Should()
            .Be(1);

        using var provider = services.BuildServiceProvider();
        using var scope = provider.CreateScope();
        scope.ServiceProvider.GetServices<ICoachingRelationshipAccessService>()
            .Should()
            .ContainSingle()
            .Which
            .Should()
            .BeOfType<CoachingRelationshipAccessService>();
    }
}
