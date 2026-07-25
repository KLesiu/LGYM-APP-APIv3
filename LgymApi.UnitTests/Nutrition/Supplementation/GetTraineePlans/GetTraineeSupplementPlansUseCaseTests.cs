using FluentAssertions;
using LgymApi.Application.Coaching.Contracts.Access;
using LgymApi.Application.BuildingBlocks.Errors;
using LgymApi.Application.Nutrition.Errors;
using LgymApi.Application.Mapping;
using LgymApi.Application.Mapping.Core;
using LgymApi.Application.Nutrition.Persistence;
using LgymApi.Application.Nutrition.Supplementation.GetTraineePlans;
using LgymApi.Domain.Entities;
using LgymApi.Domain.ValueObjects;
using LgymApi.Resources;
using Microsoft.Extensions.DependencyInjection;
using NSubstitute;
using UserEntity = LgymApi.Domain.Entities.User;

namespace LgymApi.UnitTests.Nutrition.Supplementation.GetTraineePlans;

[TestFixture]
public sealed class GetTraineeSupplementPlansUseCaseTests
{
    [Test]
    public async Task ExecuteAsync_WhenTrainerIsLinked_MapsTheNoTrackingPlansInLegacyOrder()
    {
        var trainerId = Id<UserEntity>.New();
        var traineeId = Id<UserEntity>.New();
        var plans = new List<SupplementPlan>
        {
            CreatePlan(trainerId, traineeId, "Newest", new DateTimeOffset(2026, 7, 3, 8, 0, 0, TimeSpan.Zero), 2),
            CreatePlan(trainerId, traineeId, "Older", new DateTimeOffset(2026, 7, 2, 8, 0, 0, TimeSpan.Zero), 1)
        };
        plans[0].Items.Add(CreateItem(plans[0], "Later", 2, 10, new TimeSpan(12, 0, 0)));
        plans[0].Items.Add(CreateItem(plans[0], "Earlier", 1, 1, new TimeSpan(8, 0, 0)));
        var dependencies = new Dependencies();
        dependencies.Access.GetAccessDecisionAsync(trainerId, traineeId, CancellationToken.None)
            .Returns(new CoachingRelationshipAccessDecision(true, true));
        dependencies.Plans.ListPlansByTrainerAndTraineeAsync(trainerId, traineeId, CancellationToken.None)
            .Returns(plans);

        var result = await dependencies.CreateUseCase().ExecuteAsync(
            new GetTraineeSupplementPlansQuery(trainerId, traineeId));

        result.IsSuccess.Should().BeTrue();
        result.Value.Select(plan => plan.Name).Should().Equal("Newest", "Older");
        result.Value[0].Items.Select(item => item.SupplementName).Should().Equal("Earlier", "Later");
        await dependencies.Access.Received(1).GetAccessDecisionAsync(trainerId, traineeId, CancellationToken.None);
        await dependencies.Plans.Received(1).ListPlansByTrainerAndTraineeAsync(trainerId, traineeId, CancellationToken.None);
        dependencies.Plans.ReceivedCalls().Should().ContainSingle();
    }

    [Test]
    public async Task ExecuteAsync_WhenRequesterIsNotTrainer_ReturnsForbiddenBeforeEmptyIdCheck()
    {
        var trainerId = Id<UserEntity>.New();
        var dependencies = new Dependencies();
        dependencies.Access.GetAccessDecisionAsync(trainerId, Id<UserEntity>.Empty, CancellationToken.None)
            .Returns(new CoachingRelationshipAccessDecision(false, true));

        var result = await dependencies.CreateUseCase().ExecuteAsync(
            new GetTraineeSupplementPlansQuery(trainerId, Id<UserEntity>.Empty));

        result.Error.Should().BeOfType<SupplementationForbiddenError>();
        result.Error.Message.Should().Be(Messages.TrainerRoleRequired);
        result.Error.HttpStatusCode.Should().Be(403);
        dependencies.Plans.ReceivedCalls().Should().BeEmpty();
    }

    [Test]
    public async Task ExecuteAsync_WhenTrainerIdIsEmpty_ReturnsBadRequestAfterCoaching()
    {
        var trainerId = Id<UserEntity>.New();
        var dependencies = new Dependencies();
        dependencies.Access.GetAccessDecisionAsync(trainerId, Id<UserEntity>.Empty, CancellationToken.None)
            .Returns(new CoachingRelationshipAccessDecision(true, true));

        var result = await dependencies.CreateUseCase().ExecuteAsync(
            new GetTraineeSupplementPlansQuery(trainerId, Id<UserEntity>.Empty));

        result.Error.Should().BeOfType<InvalidSupplementationError>();
        result.Error.Message.Should().Be(Messages.UserIdRequired);
        await dependencies.Access.Received(1).GetAccessDecisionAsync(trainerId, Id<UserEntity>.Empty, CancellationToken.None);
        dependencies.Plans.ReceivedCalls().Should().BeEmpty();
    }

    [Test]
    public async Task ExecuteAsync_WhenTrainerHasNoActiveLink_ReturnsNotFoundBeforePersistence()
    {
        var trainerId = Id<UserEntity>.New();
        var traineeId = Id<UserEntity>.New();
        var dependencies = new Dependencies();
        dependencies.Access.GetAccessDecisionAsync(trainerId, traineeId, CancellationToken.None)
            .Returns(new CoachingRelationshipAccessDecision(true, false));

        var result = await dependencies.CreateUseCase().ExecuteAsync(
            new GetTraineeSupplementPlansQuery(trainerId, traineeId));

        result.Error.Should().BeOfType<SupplementationNotFoundError>();
        result.Error.Message.Should().Be(Messages.DidntFind);
        dependencies.Plans.ReceivedCalls().Should().BeEmpty();
    }

    [Test]
    public async Task ExecuteAsync_ForwardsCancellationToAccessAndNoTrackingPersistence()
    {
        var trainerId = Id<UserEntity>.New();
        var traineeId = Id<UserEntity>.New();
        using var cancellationSource = new CancellationTokenSource();
        var cancellationToken = cancellationSource.Token;
        var dependencies = new Dependencies();
        dependencies.Access.GetAccessDecisionAsync(trainerId, traineeId, cancellationToken)
            .Returns(new CoachingRelationshipAccessDecision(true, true));
        dependencies.Plans.ListPlansByTrainerAndTraineeAsync(trainerId, traineeId, cancellationToken)
            .Returns([]);

        var result = await dependencies.CreateUseCase().ExecuteAsync(
            new GetTraineeSupplementPlansQuery(trainerId, traineeId),
            cancellationToken);

        result.IsSuccess.Should().BeTrue();
        await dependencies.Access.Received(1).GetAccessDecisionAsync(trainerId, traineeId, cancellationToken);
        await dependencies.Plans.Received(1).ListPlansByTrainerAndTraineeAsync(trainerId, traineeId, cancellationToken);
        dependencies.Plans.ReceivedCalls().Should().ContainSingle();
    }

    private static SupplementPlan CreatePlan(
        Id<UserEntity> trainerId,
        Id<UserEntity> traineeId,
        string name,
        DateTimeOffset createdAt,
        int order)
        => new()
        {
            Id = Id<SupplementPlan>.New(),
            TrainerId = trainerId,
            TraineeId = traineeId,
            Name = name,
            IsActive = order == 2,
            CreatedAt = createdAt,
            UpdatedAt = createdAt
        };

    private static SupplementPlanItem CreateItem(
        SupplementPlan plan,
        string name,
        int order,
        int daysOfWeekMask,
        TimeSpan timeOfDay)
        => new()
        {
            Id = Id<SupplementPlanItem>.New(),
            PlanId = plan.Id,
            SupplementName = name,
            Dosage = "1",
            Order = order,
            DaysOfWeekMask = (DaysOfWeekSet)daysOfWeekMask,
            TimeOfDay = timeOfDay,
            CreatedAt = plan.CreatedAt
        };

    private sealed class Dependencies
    {
        public ICoachingRelationshipAccessService Access { get; } = Substitute.For<ICoachingRelationshipAccessService>();
        public ISupplementationPersistence Plans { get; } = Substitute.For<ISupplementationPersistence>();
        public IMapper Mapper { get; } = CreateMapper();

        public IGetTraineeSupplementPlansUseCase CreateUseCase()
            => new GetTraineeSupplementPlansUseCase(Access, Plans, Mapper);

        private static IMapper CreateMapper()
        {
            var services = new ServiceCollection();
            services.AddApplicationMapping(typeof(IMappingProfile).Assembly);

            using var provider = services.BuildServiceProvider();
            return provider.GetRequiredService<IMapper>();
        }
    }
}
