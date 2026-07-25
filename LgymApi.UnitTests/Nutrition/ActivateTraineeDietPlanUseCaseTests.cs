using FluentAssertions;
using LgymApi.Application.Coaching.Contracts.Access;
using LgymApi.Application.BuildingBlocks.Errors;
using LgymApi.Application.Coaching.Errors;
using LgymApi.Application.Mapping;
using LgymApi.Application.Mapping.Core;
using LgymApi.Application.Nutrition.Contracts.BackgroundCommands;
using LgymApi.Application.Nutrition.DietPlans;
using LgymApi.Application.Nutrition.DietPlans.ActivateTraineePlan;
using LgymApi.Application.Nutrition.DietPlans.ActivateTraineePlan.Models;
using LgymApi.Application.Nutrition.Persistence;
using LgymApi.Application.Platform.Contracts.BackgroundCommands;
using LgymApi.Application.Repositories;
using LgymApi.Domain.Entities;
using LgymApi.Domain.ValueObjects;
using LgymApi.Resources;
using Microsoft.Extensions.DependencyInjection;
using NSubstitute;
using NUnit.Framework;

namespace LgymApi.UnitTests.Nutrition;

[TestFixture]
public sealed class ActivateTraineeDietPlanUseCaseTests
{
    [Test]
    public async Task ExecuteAsync_StagesActivatedHistorySavesThenEnqueues()
    {
        var trainerId = Id<User>.New();
        var traineeId = Id<User>.New();
        var plan = CreatePlan(trainerId, traineeId);
        var operations = new List<string>();
        var dependencies = new Dependencies();
        DietPlanHistory? stagedHistory = null;
        DietPlanUpdatedInAppNotificationCommand? notification = null;
        dependencies.Access.GetAccessDecisionAsync(trainerId, traineeId, Arg.Any<CancellationToken>())
            .Returns(new CoachingRelationshipAccessDecision(true, true))
            .AndDoes(_ => operations.Add("access"));
        dependencies.Plans.FindTrackedPlanByIdAsync(plan.Id, Arg.Any<CancellationToken>())
            .Returns(plan)
            .AndDoes(_ => operations.Add("plan"));
        dependencies.Plans.AddHistoryEntryAsync(Arg.Any<DietPlanHistory>(), Arg.Any<CancellationToken>())
            .Returns(Task.CompletedTask)
            .AndDoes(call =>
            {
                stagedHistory = call.Arg<DietPlanHistory>();
                operations.Add("history");
            });
        dependencies.UnitOfWork.SaveChangesAsync(Arg.Any<CancellationToken>())
            .Returns(1)
            .AndDoes(_ => operations.Add("save"));
        dependencies.Commands.EnqueueAsync(Arg.Any<DietPlanUpdatedInAppNotificationCommand>())
            .Returns(Task.CompletedTask)
            .AndDoes(call =>
            {
                notification = call.Arg<DietPlanUpdatedInAppNotificationCommand>();
                operations.Add("notification");
            });

        var result = await dependencies.CreateUseCase().ExecuteAsync(new(trainerId, traineeId, plan.Id));

        result.IsSuccess.Should().BeTrue();
        plan.IsActive.Should().BeTrue();
        stagedHistory.Should().NotBeNull();
        stagedHistory!.DietPlanId.Should().Be(plan.Id);
        stagedHistory.ChangedByUserId.Should().Be(trainerId);
        stagedHistory.ChangeType.Should().Be("Activated");
        stagedHistory.SnapshotJson.Should().Contain("\"IsActive\":true");
        notification.Should().NotBeNull();
        notification!.DietPlanId.Should().Be(plan.Id);
        notification.TraineeId.Should().Be(traineeId);
        notification.TrainerId.Should().Be(trainerId);
        notification.DietPlanName.Should().Be(plan.Name);
        operations.Should().Equal("access", "plan", "history", "save", "notification");
        await dependencies.Plans.DidNotReceive().GetPlanByIdAsync(Arg.Any<Id<DietPlan>>(), Arg.Any<CancellationToken>());
    }

    [Test]
    public async Task ExecuteAsync_PreservesExistingActiveSibling()
    {
        var trainerId = Id<User>.New();
        var traineeId = Id<User>.New();
        var existingActivePlan = CreatePlan(trainerId, traineeId, isActive: true);
        var planToActivate = CreatePlan(trainerId, traineeId);
        var plans = new List<DietPlan> { existingActivePlan, planToActivate };
        var dependencies = new Dependencies();
        dependencies.GrantAccess(trainerId, traineeId);
        dependencies.Plans.FindTrackedPlanByIdAsync(planToActivate.Id, Arg.Any<CancellationToken>()).Returns(planToActivate);
        dependencies.Plans.AddHistoryEntryAsync(Arg.Any<DietPlanHistory>(), Arg.Any<CancellationToken>()).Returns(Task.CompletedTask);
        dependencies.UnitOfWork.SaveChangesAsync(Arg.Any<CancellationToken>()).Returns(1);
        dependencies.Commands.EnqueueAsync(Arg.Any<DietPlanUpdatedInAppNotificationCommand>()).Returns(Task.CompletedTask);

        var result = await dependencies.CreateUseCase().ExecuteAsync(new(trainerId, traineeId, planToActivate.Id));

        result.IsSuccess.Should().BeTrue();
        existingActivePlan.IsActive.Should().BeTrue();
        plans.Count(plan => plan.IsActive).Should().Be(2);
        await dependencies.Plans.DidNotReceive().ListPlansByTrainerAndTraineeAsync(
            Arg.Any<Id<User>>(),
            Arg.Any<Id<User>>(),
            Arg.Any<CancellationToken>());
        await dependencies.Plans.DidNotReceive().ListActivePlansForTraineeAsync(
            Arg.Any<Id<User>>(),
            Arg.Any<CancellationToken>());
    }

    [Test]
    public async Task ExecuteAsync_WhenCallerIsNotTrainer_ReturnsForbiddenWithoutWrites()
    {
        var trainerId = Id<User>.New();
        var traineeId = Id<User>.New();
        var dependencies = new Dependencies();
        dependencies.Access.GetAccessDecisionAsync(trainerId, traineeId, Arg.Any<CancellationToken>())
            .Returns(new CoachingRelationshipAccessDecision(false, true));

        var result = await dependencies.CreateUseCase().ExecuteAsync(new(trainerId, traineeId, Id<DietPlan>.New()));

        result.Error.Should().BeOfType<TrainerRelationshipForbiddenError>();
        result.Error.Message.Should().Be(Messages.TrainerRoleRequired);
        await dependencies.Plans.DidNotReceive().FindTrackedPlanByIdAsync(Arg.Any<Id<DietPlan>>(), Arg.Any<CancellationToken>());
        await AssertNoWritesAsync(dependencies);
    }

    [Test]
    public async Task ExecuteAsync_WhenRelationshipIsUnavailable_ReturnsNotFoundWithoutWrites()
    {
        var trainerId = Id<User>.New();
        var traineeId = Id<User>.New();
        var dependencies = new Dependencies();
        dependencies.Access.GetAccessDecisionAsync(trainerId, traineeId, Arg.Any<CancellationToken>())
            .Returns(new CoachingRelationshipAccessDecision(true, false));

        var result = await dependencies.CreateUseCase().ExecuteAsync(new(trainerId, traineeId, Id<DietPlan>.New()));

        result.Error.Should().BeOfType<NotFoundError>();
        result.Error.Message.Should().Be(Messages.DidntFind);
        await dependencies.Plans.DidNotReceive().FindTrackedPlanByIdAsync(Arg.Any<Id<DietPlan>>(), Arg.Any<CancellationToken>());
        await AssertNoWritesAsync(dependencies);
    }

    [Test]
    public async Task ExecuteAsync_WhenIdentifiersOrPlanOwnershipAreUnavailable_ReturnsFailureWithoutWrites()
    {
        var trainerId = Id<User>.New();
        var traineeId = Id<User>.New();

        var emptyTraineeDependencies = new Dependencies();
        var emptyTraineeResult = await emptyTraineeDependencies.CreateUseCase().ExecuteAsync(
            new(trainerId, Id<User>.Empty, Id<DietPlan>.New()));

        emptyTraineeResult.Error.Should().BeOfType<BadRequestError>();
        emptyTraineeResult.Error.Message.Should().Be(Messages.UserIdRequired);
        await emptyTraineeDependencies.Access.DidNotReceive().GetAccessDecisionAsync(
            Arg.Any<Id<User>>(),
            Arg.Any<Id<User>>(),
            Arg.Any<CancellationToken>());
        await AssertNoWritesAsync(emptyTraineeDependencies);

        var emptyPlanDependencies = new Dependencies();
        emptyPlanDependencies.GrantAccess(trainerId, traineeId);
        var emptyPlanResult = await emptyPlanDependencies.CreateUseCase().ExecuteAsync(
            new(trainerId, traineeId, Id<DietPlan>.Empty));

        emptyPlanResult.Error.Should().BeOfType<BadRequestError>();
        emptyPlanResult.Error.Message.Should().Be(Messages.FieldRequired);
        await emptyPlanDependencies.Plans.DidNotReceive().FindTrackedPlanByIdAsync(
            Arg.Any<Id<DietPlan>>(),
            Arg.Any<CancellationToken>());
        await AssertNoWritesAsync(emptyPlanDependencies);

        var missingPlanDependencies = new Dependencies();
        missingPlanDependencies.GrantAccess(trainerId, traineeId);
        missingPlanDependencies.Plans.FindTrackedPlanByIdAsync(Arg.Any<Id<DietPlan>>(), Arg.Any<CancellationToken>())
            .Returns((DietPlan?)null);
        var missingPlanResult = await missingPlanDependencies.CreateUseCase().ExecuteAsync(
            new(trainerId, traineeId, Id<DietPlan>.New()));

        missingPlanResult.Error.Should().BeOfType<NotFoundError>();
        await AssertNoWritesAsync(missingPlanDependencies);

        var foreignPlanDependencies = new Dependencies();
        foreignPlanDependencies.GrantAccess(trainerId, traineeId);
        foreignPlanDependencies.Plans.FindTrackedPlanByIdAsync(Arg.Any<Id<DietPlan>>(), Arg.Any<CancellationToken>())
            .Returns(CreatePlan(Id<User>.New(), traineeId));
        var foreignPlanResult = await foreignPlanDependencies.CreateUseCase().ExecuteAsync(
            new(trainerId, traineeId, Id<DietPlan>.New()));

        foreignPlanResult.Error.Should().BeOfType<NotFoundError>();
        await AssertNoWritesAsync(foreignPlanDependencies);
    }

    [Test]
    public async Task ExecuteAsync_WhenSaveFails_DoesNotEnqueueNotification()
    {
        var trainerId = Id<User>.New();
        var traineeId = Id<User>.New();
        var plan = CreatePlan(trainerId, traineeId);
        var dependencies = new Dependencies();
        dependencies.GrantAccess(trainerId, traineeId);
        dependencies.Plans.FindTrackedPlanByIdAsync(plan.Id, Arg.Any<CancellationToken>()).Returns(plan);
        dependencies.Plans.AddHistoryEntryAsync(Arg.Any<DietPlanHistory>(), Arg.Any<CancellationToken>()).Returns(Task.CompletedTask);
        dependencies.UnitOfWork.SaveChangesAsync(Arg.Any<CancellationToken>())
            .Returns(Task.FromException<int>(new InvalidOperationException("commit failed")));

        Func<Task> act = () => dependencies.CreateUseCase().ExecuteAsync(new(trainerId, traineeId, plan.Id));

        await act.Should().ThrowAsync<InvalidOperationException>().WithMessage("commit failed");
        await dependencies.Commands.DidNotReceive().EnqueueAsync(Arg.Any<DietPlanUpdatedInAppNotificationCommand>());
    }

    private static DietPlan CreatePlan(Id<User> trainerId, Id<User> traineeId, bool isActive = false)
        => new()
        {
            Id = Id<DietPlan>.New(),
            TrainerId = trainerId,
            TraineeId = traineeId,
            Name = "Nutrition plan",
            IsActive = isActive
        };

    private static async Task AssertNoWritesAsync(Dependencies dependencies)
    {
        await dependencies.Plans.DidNotReceive().AddHistoryEntryAsync(
            Arg.Any<DietPlanHistory>(),
            Arg.Any<CancellationToken>());
        await dependencies.UnitOfWork.DidNotReceive().SaveChangesAsync(Arg.Any<CancellationToken>());
        await dependencies.Commands.DidNotReceive().EnqueueAsync(Arg.Any<DietPlanUpdatedInAppNotificationCommand>());
    }

    private sealed class Dependencies
    {
        public ICoachingRelationshipAccessService Access { get; } = Substitute.For<ICoachingRelationshipAccessService>();
        public IDietPlanPersistence Plans { get; } = Substitute.For<IDietPlanPersistence>();
        public ICommandDispatcher Commands { get; } = Substitute.For<ICommandDispatcher>();
        public IUnitOfWork UnitOfWork { get; } = Substitute.For<IUnitOfWork>();

        public void GrantAccess(Id<User> trainerId, Id<User> traineeId)
            => Access.GetAccessDecisionAsync(trainerId, traineeId, Arg.Any<CancellationToken>())
                .Returns(new CoachingRelationshipAccessDecision(true, true));

        public ActivateTraineeDietPlanUseCase CreateUseCase()
            => new(Access, Plans, Commands, UnitOfWork, new DietPlanHistorySnapshotFactory(CreateMapper()));

        private static IMapper CreateMapper()
        {
            var services = new ServiceCollection();
            services.AddApplicationMapping(typeof(IMappingProfile).Assembly);

            using var provider = services.BuildServiceProvider();
            return provider.GetRequiredService<IMapper>();
        }
    }
}
