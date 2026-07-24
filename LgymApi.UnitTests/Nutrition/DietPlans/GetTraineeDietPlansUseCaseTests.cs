using FluentAssertions;
using LgymApi.Application.Coaching.Contracts.Access;
using LgymApi.Application.Common.Errors;
using LgymApi.Application.Mapping;
using LgymApi.Application.Mapping.Core;
using LgymApi.Application.Nutrition.DietPlans.GetTraineePlans;
using LgymApi.Application.Nutrition.Persistence;
using LgymApi.Domain.Entities;
using LgymApi.Domain.ValueObjects;
using LgymApi.Resources;
using Microsoft.Extensions.DependencyInjection;
using NSubstitute;
using UserEntity = LgymApi.Domain.Entities.User;

namespace LgymApi.UnitTests.Nutrition.DietPlans;

[TestFixture]
public sealed class GetTraineeDietPlansUseCaseTests
{
    [Test]
    public async Task ExecuteAsync_WhenTrainerIsLinked_MapsTheNoTrackingPlansInLegacyOrder()
    {
        var trainerId = Id<UserEntity>.New();
        var traineeId = Id<UserEntity>.New();
        var plans = new List<DietPlan>
        {
            CreatePlan(trainerId, traineeId, "Active latest start", true, new DateOnly(2026, 7, 3), new DateTimeOffset(2026, 7, 1, 8, 0, 0, TimeSpan.Zero)),
            CreatePlan(trainerId, traineeId, "Active same start newer created", true, new DateOnly(2026, 7, 2), new DateTimeOffset(2026, 7, 2, 8, 0, 0, TimeSpan.Zero)),
            CreatePlan(trainerId, traineeId, "Active same start older created", true, new DateOnly(2026, 7, 2), new DateTimeOffset(2026, 7, 1, 8, 0, 0, TimeSpan.Zero)),
            CreatePlan(trainerId, traineeId, "Inactive", false, new DateOnly(2026, 7, 4), new DateTimeOffset(2026, 7, 4, 8, 0, 0, TimeSpan.Zero))
        };
        var dependencies = new Dependencies();
        dependencies.Access.GetAccessDecisionAsync(trainerId, traineeId, CancellationToken.None)
            .Returns(new CoachingRelationshipAccessDecision(true, true));
        dependencies.Plans.ListPlansByTrainerAndTraineeAsync(trainerId, traineeId, CancellationToken.None)
            .Returns(plans);

        var result = await dependencies.CreateUseCase().ExecuteAsync(new GetTraineeDietPlansQuery(trainerId, traineeId));

        result.IsSuccess.Should().BeTrue();
        result.Value.Select(plan => plan.Name).Should().Equal(plans.Select(plan => plan.Name));
        result.Value.Select(plan => plan.IsActive).Should().Equal(true, true, true, false);
        result.Value.Select(plan => plan.StartDate).Should().Equal(
            new DateOnly(2026, 7, 3),
            new DateOnly(2026, 7, 2),
            new DateOnly(2026, 7, 2),
            new DateOnly(2026, 7, 4));
        result.Value.Select(plan => plan.CreatedAt).Should().Equal(plans.Select(plan => plan.CreatedAt));
        await dependencies.Access.Received(1).GetAccessDecisionAsync(trainerId, traineeId, CancellationToken.None);
        await dependencies.Plans.Received(1).ListPlansByTrainerAndTraineeAsync(trainerId, traineeId, CancellationToken.None);
        dependencies.Plans.ReceivedCalls().Should().ContainSingle();
    }

    [Test]
    public async Task ExecuteAsync_WhenTraineeIdIsEmpty_ReturnsBadRequestWithoutCoachingAccess()
    {
        var trainerId = Id<UserEntity>.New();
        var dependencies = new Dependencies();

        var result = await dependencies.CreateUseCase().ExecuteAsync(
            new GetTraineeDietPlansQuery(trainerId, Id<UserEntity>.Empty));

        result.Error.Should().BeOfType<BadRequestError>();
        result.Error.Message.Should().Be(Messages.UserIdRequired);
        await dependencies.Access.DidNotReceive().GetAccessDecisionAsync(
            Arg.Any<Id<UserEntity>>(),
            Arg.Any<Id<UserEntity>>(),
            Arg.Any<CancellationToken>());
        dependencies.Plans.ReceivedCalls().Should().BeEmpty();
    }

    [Test]
    public async Task ExecuteAsync_WhenRequesterIsNotTrainer_ReturnsForbiddenBeforePersistence()
    {
        var trainerId = Id<UserEntity>.New();
        var traineeId = Id<UserEntity>.New();
        var dependencies = new Dependencies();
        dependencies.Access.GetAccessDecisionAsync(trainerId, traineeId, CancellationToken.None)
            .Returns(new CoachingRelationshipAccessDecision(false, false));

        var result = await dependencies.CreateUseCase().ExecuteAsync(new GetTraineeDietPlansQuery(trainerId, traineeId));

        result.Error.Should().BeOfType<TrainerRelationshipForbiddenError>();
        result.Error.Message.Should().Be(Messages.TrainerRoleRequired);
        result.Error.HttpStatusCode.Should().Be(403);
        await dependencies.Access.Received(1).GetAccessDecisionAsync(trainerId, traineeId, CancellationToken.None);
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

        var result = await dependencies.CreateUseCase().ExecuteAsync(new GetTraineeDietPlansQuery(trainerId, traineeId));

        result.Error.Should().BeOfType<NotFoundError>();
        result.Error.Message.Should().Be(Messages.DidntFind);
        await dependencies.Access.Received(1).GetAccessDecisionAsync(trainerId, traineeId, CancellationToken.None);
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
            new GetTraineeDietPlansQuery(trainerId, traineeId),
            cancellationToken);

        result.IsSuccess.Should().BeTrue();
        await dependencies.Access.Received(1).GetAccessDecisionAsync(trainerId, traineeId, cancellationToken);
        await dependencies.Plans.Received(1).ListPlansByTrainerAndTraineeAsync(trainerId, traineeId, cancellationToken);
        dependencies.Plans.ReceivedCalls().Should().ContainSingle();
    }

    private static DietPlan CreatePlan(
        Id<UserEntity> trainerId,
        Id<UserEntity> traineeId,
        string name,
        bool isActive,
        DateOnly startDate,
        DateTimeOffset createdAt)
        => new()
        {
            Id = Id<DietPlan>.New(),
            TrainerId = trainerId,
            TraineeId = traineeId,
            Name = name,
            StartDate = startDate,
            IsActive = isActive,
            CreatedAt = createdAt,
            UpdatedAt = createdAt
        };

    private sealed class Dependencies
    {
        public ICoachingRelationshipAccessService Access { get; } = Substitute.For<ICoachingRelationshipAccessService>();
        public IDietPlanPersistence Plans { get; } = Substitute.For<IDietPlanPersistence>();
        public IMapper Mapper { get; } = CreateMapper();

        public IGetTraineeDietPlansUseCase CreateUseCase()
            => new GetTraineeDietPlansUseCase(Access, Plans, Mapper);

        private static IMapper CreateMapper()
        {
            var services = new ServiceCollection();
            services.AddApplicationMapping(typeof(IMappingProfile).Assembly);

            using var provider = services.BuildServiceProvider();
            return provider.GetRequiredService<IMapper>();
        }
    }
}
