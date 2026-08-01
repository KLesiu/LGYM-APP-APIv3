using FluentAssertions;
using LgymApi.Application;
using LgymApi.Application.Coaching;
using LgymApi.Application.Coaching.Adapters;
using LgymApi.Application.Coaching.Contracts.Access;
using LgymApi.Application.Coaching.Persistence;
using LgymApi.Application.Identity.Contracts.Access;
using LgymApi.Identity.Contracts.Accounts;
using LgymApi.Application.TrainingPlanning.Contracts.PlanDay;
using LgymApi.Domain.ValueObjects;
using LgymApi.Identity.Contracts;
using Microsoft.Extensions.DependencyInjection;
using NSubstitute;

namespace LgymApi.UnitTests;

[TestFixture]
public sealed class PlanDayRelationshipAccessAdapterTests
{
    [TestCase(true)]
    [TestCase(false)]
    public async Task HasActiveRelationshipAsync_ReturnsCoachingRelationshipDecision(bool hasActiveRelationship)
    {
        var trainerId = Id<AccountReference>.New();
        var traineeId = Id<AccountReference>.New();
        var relationshipAccess = Substitute.For<IMarkerCoachingRelationshipAccessService>();
        relationshipAccess.GetAccessDecisionAsync(trainerId, traineeId, CancellationToken.None)
            .Returns(new CoachingRelationshipAccessDecision(true, hasActiveRelationship));
        var adapter = new PlanDayRelationshipAccessAdapter(relationshipAccess);

        var result = await adapter.HasActiveRelationshipAsync(trainerId, traineeId);

        result.Should().Be(hasActiveRelationship);
        await relationshipAccess.Received(1)
            .GetAccessDecisionAsync(trainerId, traineeId, CancellationToken.None);
    }

    [Test]
    public async Task HasActiveRelationshipAsync_ForwardsTypedIdsAndCancellation()
    {
        var trainerId = Id<AccountReference>.New();
        var traineeId = Id<AccountReference>.New();
        using var cancellationSource = new CancellationTokenSource();
        var cancellationToken = cancellationSource.Token;
        var relationshipAccess = Substitute.For<IMarkerCoachingRelationshipAccessService>();
        relationshipAccess.GetAccessDecisionAsync(trainerId, traineeId, cancellationToken)
            .Returns(new CoachingRelationshipAccessDecision(true, false));
        var adapter = new PlanDayRelationshipAccessAdapter(relationshipAccess);

        await adapter.HasActiveRelationshipAsync(trainerId, traineeId, cancellationToken);

        await relationshipAccess.Received(1)
            .GetAccessDecisionAsync(trainerId, traineeId, cancellationToken);
    }

    [Test]
    public void AddApplication_RegistersPlanDayRelationshipAccessAdapterExactlyOnceAndResolvesIt()
    {
        var services = new ServiceCollection();
        services.AddApplication();
        services.AddScoped(_ => Substitute.For<IUserAccessReadService>());
        services.AddScoped(_ => Substitute.For<IAccountAccessReader>());
        services.AddScoped(_ => Substitute.For<ICoachingActiveLinkPersistence>());

        services.Count(descriptor => descriptor.ServiceType == typeof(IPlanDayRelationshipAccessPort))
            .Should()
            .Be(1);

        using var provider = services.BuildServiceProvider();
        using var scope = provider.CreateScope();
        scope.ServiceProvider.GetServices<IPlanDayRelationshipAccessPort>()
            .Should()
            .ContainSingle()
            .Which
            .Should()
            .BeOfType<PlanDayRelationshipAccessAdapter>();
    }
}
