using FluentAssertions;
using LgymApi.Application.Coaching.Contracts.Access;
using LgymApi.Application.BuildingBlocks.Errors;
using LgymApi.Application.Coaching.Errors;
using LgymApi.Application.Mapping.Core;
using LgymApi.Application.Nutrition.DietPlans.GetTraineePlanHistory;
using LgymApi.Application.Nutrition.DietPlans.GetTraineePlanHistory.Contracts;
using LgymApi.Application.Nutrition.DietPlans.GetTraineePlanHistory.Models;
using LgymApi.Application.Nutrition.DietPlans.GetOwnPlanHistory;
using LgymApi.Application.Nutrition.DietPlans.Models;
using LgymApi.Application.Nutrition.Persistence;
using LgymApi.Domain.Entities;
using LgymApi.Domain.ValueObjects;
using LgymApi.Resources;
using NSubstitute;
using NUnit.Framework;

namespace LgymApi.UnitTests.Nutrition.DietPlans.GetTraineePlanHistory;

[TestFixture]
public sealed class GetTraineeDietPlanHistoryUseCaseTests
{
    [Test]
    public async Task ExecuteAsync_WhenPlanIsOwned_ReturnsDescendingHistoryWithUnchangedSnapshotBytes()
    {
        var trainerId = Id<User>.New();
        var traineeId = Id<User>.New();
        var plan = CreatePlan(trainerId, traineeId);
        var newer = CreateHistory(plan.Id, new DateTimeOffset(2026, 7, 24, 12, 0, 0, TimeSpan.Zero), "{\"known\":1,\"legacyUnknown\":true}");
        var older = CreateHistory(plan.Id, new DateTimeOffset(2026, 7, 23, 12, 0, 0, TimeSpan.Zero), "{\"known\":2,\"legacyUnknown\":false}");
        var mappedNewer = CreateReadModel(newer);
        var mappedOlder = CreateReadModel(older);
        var dependencies = new Dependencies();
        dependencies.Access.GetAccessDecisionAsync(trainerId, traineeId, Arg.Any<CancellationToken>())
            .Returns(new CoachingRelationshipAccessDecision(true, true));
        dependencies.Plans.GetPlanByIdAsync(plan.Id, Arg.Any<CancellationToken>()).Returns(plan);
        dependencies.Plans.ListPlanHistoryAsync(plan.Id, Arg.Any<CancellationToken>()).Returns([newer, older]);
        dependencies.Mapper.MapList<DietPlanHistory, DietPlanHistoryReadModel>(
                Arg.Any<IEnumerable<DietPlanHistory>>(), Arg.Any<MappingContext?>())
            .Returns([mappedNewer, mappedOlder]);

        var result = await dependencies.CreateUseCase().ExecuteAsync(
            new GetTraineeDietPlanHistoryQuery(trainerId, traineeId, plan.Id));

        result.IsSuccess.Should().BeTrue();
        result.Value.Should().Equal(mappedNewer, mappedOlder);
        result.Value.Select(history => history.SnapshotJson)
            .Should().Equal("{\"known\":1,\"legacyUnknown\":true}", "{\"known\":2,\"legacyUnknown\":false}");
        await dependencies.Plans.Received(1).GetPlanByIdAsync(plan.Id, Arg.Any<CancellationToken>());
        await dependencies.Plans.Received(1).ListPlanHistoryAsync(plan.Id, Arg.Any<CancellationToken>());
        await dependencies.Plans.DidNotReceive().FindTrackedPlanByIdAsync(Arg.Any<Id<DietPlan>>(), Arg.Any<CancellationToken>());
    }

    [Test]
    public async Task ExecuteAsync_WhenTraineeIdIsEmpty_ReturnsBadRequestBeforeCoaching()
    {
        var dependencies = new Dependencies();

        var result = await dependencies.CreateUseCase().ExecuteAsync(
            new GetTraineeDietPlanHistoryQuery(Id<User>.New(), Id<User>.Empty, Id<DietPlan>.New()));

        result.Error.Should().BeOfType<BadRequestError>();
        result.Error.Message.Should().Be(Messages.UserIdRequired);
        await AssertNoReadsAsync(dependencies);
    }

    [Test]
    public async Task ExecuteAsync_WhenCallerIsNotTrainer_ReturnsForbiddenBeforePlanLookup()
    {
        var trainerId = Id<User>.New();
        var traineeId = Id<User>.New();
        var dependencies = new Dependencies();
        dependencies.Access.GetAccessDecisionAsync(trainerId, traineeId, Arg.Any<CancellationToken>())
            .Returns(new CoachingRelationshipAccessDecision(false, true));

        var result = await dependencies.CreateUseCase().ExecuteAsync(
            new GetTraineeDietPlanHistoryQuery(trainerId, traineeId, Id<DietPlan>.New()));

        result.Error.Should().BeOfType<TrainerRelationshipForbiddenError>();
        result.Error.Message.Should().Be(Messages.TrainerRoleRequired);
        await AssertNoReadsAsync(dependencies);
    }

    [Test]
    public async Task ExecuteAsync_WhenNoActiveLinkExists_ReturnsNotFoundBeforePlanLookup()
    {
        var trainerId = Id<User>.New();
        var traineeId = Id<User>.New();
        var dependencies = new Dependencies();
        dependencies.Access.GetAccessDecisionAsync(trainerId, traineeId, Arg.Any<CancellationToken>())
            .Returns(new CoachingRelationshipAccessDecision(true, false));

        var result = await dependencies.CreateUseCase().ExecuteAsync(
            new GetTraineeDietPlanHistoryQuery(trainerId, traineeId, Id<DietPlan>.New()));

        result.Error.Should().BeOfType<NotFoundError>();
        result.Error.Message.Should().Be(Messages.DidntFind);
        await AssertNoReadsAsync(dependencies);
    }

    [Test]
    public async Task ExecuteAsync_WhenPlanIsMissingForeignOrDeleted_ReturnsNotFoundWithoutHistoryRead()
    {
        var trainerId = Id<User>.New();
        var traineeId = Id<User>.New();
        var planId = Id<DietPlan>.New();
        var dependencies = new Dependencies();
        dependencies.Access.GetAccessDecisionAsync(trainerId, traineeId, Arg.Any<CancellationToken>())
            .Returns(new CoachingRelationshipAccessDecision(true, true));
        dependencies.Plans.GetPlanByIdAsync(planId, Arg.Any<CancellationToken>())
            .Returns(null, CreatePlan(Id<User>.New(), traineeId, planId), CreatePlan(trainerId, traineeId, planId, true));

        foreach (var attempt in Enumerable.Range(0, 3))
        {
            var result = await dependencies.CreateUseCase().ExecuteAsync(
                new GetTraineeDietPlanHistoryQuery(trainerId, traineeId, planId));

            result.Error.Should().BeOfType<NotFoundError>();
            result.Error.Message.Should().Be(Messages.DidntFind);
        }

        await dependencies.Plans.Received(3).GetPlanByIdAsync(planId, Arg.Any<CancellationToken>());
        await dependencies.Plans.DidNotReceive().ListPlanHistoryAsync(Arg.Any<Id<DietPlan>>(), Arg.Any<CancellationToken>());
        dependencies.Mapper.ReceivedCalls().Should().BeEmpty();
    }

    [Test]
    public async Task OwnHistory_WhenActivePlanBelongsToTrainee_ReturnsHistoryWithoutRelationshipLookup()
    {
        var traineeId = Id<User>.New();
        var plan = CreatePlan(Id<User>.New(), traineeId);
        var history = CreateHistory(plan.Id, DateTimeOffset.UtcNow, "{\"Name\":\"Plan\"}");
        var mapped = CreateReadModel(history);
        var dependencies = new Dependencies();
        dependencies.Plans.GetPlanByIdAsync(plan.Id, Arg.Any<CancellationToken>()).Returns(plan);
        dependencies.Plans.ListPlanHistoryAsync(plan.Id, Arg.Any<CancellationToken>()).Returns([history]);
        dependencies.Mapper.MapList<DietPlanHistory, DietPlanHistoryReadModel>(
                Arg.Any<IEnumerable<DietPlanHistory>>(), Arg.Any<MappingContext?>())
            .Returns([mapped]);

        var result = await dependencies.CreateOwnUseCase().ExecuteAsync(
            new GetOwnDietPlanHistoryQuery(traineeId, plan.Id));

        result.IsSuccess.Should().BeTrue();
        result.Value.Should().ContainSingle().Which.Should().Be(mapped);
        await dependencies.Access.DidNotReceiveWithAnyArgs().GetAccessDecisionAsync(default, default, default);
    }

    [Test]
    public async Task OwnHistory_WhenPlanIsMissingForeignOrInactive_ReturnsSameNotFoundWithoutHistoryRead()
    {
        var traineeId = Id<User>.New();
        var planId = Id<DietPlan>.New();
        var foreign = CreatePlan(Id<User>.New(), Id<User>.New(), planId);
        var inactive = CreatePlan(Id<User>.New(), traineeId, planId);
        inactive.IsActive = false;
        var dependencies = new Dependencies();
        dependencies.Plans.GetPlanByIdAsync(planId, Arg.Any<CancellationToken>())
            .Returns(null, foreign, inactive);

        foreach (var _ in Enumerable.Range(0, 3))
        {
            var result = await dependencies.CreateOwnUseCase().ExecuteAsync(
                new GetOwnDietPlanHistoryQuery(traineeId, planId));
            result.Error.Should().BeOfType<NotFoundError>();
            result.Error.Message.Should().Be(Messages.DidntFind);
        }

        await dependencies.Plans.DidNotReceive().ListPlanHistoryAsync(
            Arg.Any<Id<DietPlan>>(),
            Arg.Any<CancellationToken>());
    }

    private static async Task AssertNoReadsAsync(Dependencies dependencies)
    {
        await dependencies.Plans.DidNotReceiveWithAnyArgs().GetPlanByIdAsync(default, default);
        await dependencies.Plans.DidNotReceiveWithAnyArgs().ListPlanHistoryAsync(default, default);
        dependencies.Mapper.ReceivedCalls().Should().BeEmpty();
    }

    private static DietPlan CreatePlan(Id<User> trainerId, Id<User> traineeId, Id<DietPlan>? planId = null, bool isDeleted = false)
        => new()
        {
            Id = planId ?? Id<DietPlan>.New(),
            TrainerId = trainerId,
            TraineeId = traineeId,
            Name = "Nutrition plan",
            StartDate = new DateOnly(2026, 7, 23),
            IsActive = true,
            IsDeleted = isDeleted
        };

    private static DietPlanHistory CreateHistory(Id<DietPlan> planId, DateTimeOffset changeDate, string snapshotJson)
        => new()
        {
            Id = Id<DietPlanHistory>.New(),
            DietPlanId = planId,
            ChangedByUserId = Id<User>.New(),
            ChangeDate = changeDate,
            ChangeType = "Updated",
            SnapshotJson = snapshotJson
        };

    private static DietPlanHistoryReadModel CreateReadModel(DietPlanHistory history)
        => new(history.Id, history.DietPlanId, history.ChangedByUserId, history.ChangeDate, history.ChangeType, history.SnapshotJson);

    private sealed class Dependencies
    {
        public ICoachingRelationshipAccessService Access { get; } = Substitute.For<ICoachingRelationshipAccessService>();
        public IDietPlanPersistence Plans { get; } = Substitute.For<IDietPlanPersistence>();
        public IMapper Mapper { get; } = Substitute.For<IMapper>();

        public IGetTraineeDietPlanHistoryUseCase CreateUseCase()
            => new GetTraineeDietPlanHistoryUseCase(Access, Plans, Mapper);

        public IGetOwnDietPlanHistoryUseCase CreateOwnUseCase()
            => new GetOwnDietPlanHistoryUseCase(Plans, Mapper);
    }
}
