using System.Text.Json;
using FluentAssertions;
using LgymApi.Application.Coaching.Contracts.Access;
using LgymApi.Application.BuildingBlocks.Errors;
using LgymApi.Application.Coaching.Errors;
using LgymApi.Application.Mapping;
using LgymApi.Application.Mapping.Core;
using LgymApi.Application.Nutrition.Contracts.BackgroundCommands;
using LgymApi.Application.Nutrition.DietPlans;
using LgymApi.Application.Nutrition.DietPlans.CreateTraineePlan;
using LgymApi.Application.Nutrition.DietPlans.Models;
using LgymApi.Application.Nutrition.Persistence;
using LgymApi.Application.Platform.Contracts.BackgroundCommands;
using LgymApi.Application.Repositories;
using LgymApi.Domain.Entities;
using LgymApi.Domain.ValueObjects;
using LgymApi.Resources;
using Microsoft.Extensions.DependencyInjection;
using NSubstitute;
using NUnit.Framework;

namespace LgymApi.UnitTests.Nutrition.DietPlans.CreateTraineePlan;

[TestFixture]
public sealed class CreateTraineeDietPlanUseCaseTests
{
    [Test]
    public async Task ActiveCreate_NormalizesStagesHistorySavesThenEnqueues()
    {
        var trainerId = Id<User>.New();
        var traineeId = Id<User>.New();
        var cancellationToken = new CancellationTokenSource().Token;
        var operations = new List<string>();
        var dependencies = new Dependencies();
        DietPlan? stagedPlan = null;
        DietPlanHistory? stagedHistory = null;
        DietPlanUpdatedInAppNotificationCommand? notification = null;
        dependencies.GrantAccess(trainerId, traineeId, operations);
        dependencies.Plans.AddPlanAsync(Arg.Any<DietPlan>(), cancellationToken)
            .Returns(Task.CompletedTask)
            .AndDoes(call =>
            {
                stagedPlan = call.Arg<DietPlan>();
                operations.Add("stage plan");
            });
        dependencies.Plans.AddHistoryEntryAsync(Arg.Any<DietPlanHistory>(), cancellationToken)
            .Returns(Task.CompletedTask)
            .AndDoes(call =>
            {
                stagedHistory = call.Arg<DietPlanHistory>();
                operations.Add("stage history");
            });
        dependencies.UnitOfWork.SaveChangesAsync(cancellationToken)
            .Returns(Task.FromResult(2))
            .AndDoes(_ => operations.Add("save"));
        dependencies.Commands.EnqueueAsync(Arg.Any<DietPlanUpdatedInAppNotificationCommand>())
            .Returns(Task.CompletedTask)
            .AndDoes(call =>
            {
                notification = call.Arg<DietPlanUpdatedInAppNotificationCommand>();
                operations.Add("enqueue");
            });
        var beforeNotification = DateTimeOffset.UtcNow;

        var result = await dependencies.CreateUseCase().ExecuteAsync(
            new CreateTraineeDietPlanCommand(trainerId, traineeId, ValidData()),
            cancellationToken);

        var afterNotification = DateTimeOffset.UtcNow;
        result.IsSuccess.Should().BeTrue();
        result.Value.Name.Should().Be("Cut");
        result.Value.Notes.Should().BeNull();
        result.Value.Meals.Select(meal => meal.Name).Should().Equal("Breakfast", "Dinner");
        stagedPlan.Should().NotBeNull();
        stagedPlan!.Id.IsEmpty.Should().BeFalse();
        stagedPlan.TrainerId.Should().Be(trainerId);
        stagedPlan.TraineeId.Should().Be(traineeId);
        stagedPlan.IsDeleted.Should().BeFalse();
        stagedPlan.Meals.Should().OnlyContain(meal => !meal.Id.IsEmpty && meal.DietPlanId == stagedPlan.Id);
        stagedPlan.Meals.Select(meal => meal.Description).Should().Equal("eggs", null);
        stagedHistory.Should().NotBeNull();
        stagedHistory!.Id.IsEmpty.Should().BeFalse();
        stagedHistory.DietPlanId.Should().Be(stagedPlan.Id);
        stagedHistory.ChangedByUserId.Should().Be(trainerId);
        stagedHistory.ChangeType.Should().Be("Created");
        using (var snapshot = JsonDocument.Parse(stagedHistory.SnapshotJson))
        {
            snapshot.RootElement.GetProperty("Name").GetString().Should().Be("Cut");
            snapshot.RootElement.GetProperty("Meals").EnumerateArray()
                .Select(meal => meal.GetProperty("Name").GetString())
                .Should().Equal("Breakfast", "Dinner");
        }

        notification.Should().NotBeNull();
        notification!.DietPlanId.Should().Be(stagedPlan.Id);
        notification.TraineeId.Should().Be(traineeId);
        notification.TrainerId.Should().Be(trainerId);
        notification.DietPlanName.Should().Be("Cut");
        notification.TriggeredAt.Should().BeOnOrAfter(beforeNotification).And.BeOnOrBefore(afterNotification);
        operations.Should().Equal("access", "stage plan", "stage history", "save", "enqueue");
        await dependencies.UnitOfWork.Received(1).SaveChangesAsync(cancellationToken);
        await dependencies.UnitOfWork.DidNotReceive().BeginTransactionAsync(Arg.Any<CancellationToken>());
    }

    [Test]
    public async Task InactiveCreate_SavesWithoutEnqueueing()
    {
        var trainerId = Id<User>.New();
        var traineeId = Id<User>.New();
        var cancellationToken = new CancellationTokenSource().Token;
        var dependencies = new Dependencies();
        dependencies.GrantAccess(trainerId, traineeId);
        dependencies.Plans.AddPlanAsync(Arg.Any<DietPlan>(), cancellationToken).Returns(Task.CompletedTask);
        dependencies.Plans.AddHistoryEntryAsync(Arg.Any<DietPlanHistory>(), cancellationToken).Returns(Task.CompletedTask);
        dependencies.UnitOfWork.SaveChangesAsync(cancellationToken).Returns(Task.FromResult(2));

        var result = await dependencies.CreateUseCase().ExecuteAsync(
            new CreateTraineeDietPlanCommand(trainerId, traineeId, ValidData(isActive: false)),
            cancellationToken);

        result.IsSuccess.Should().BeTrue();
        await dependencies.UnitOfWork.Received(1).SaveChangesAsync(cancellationToken);
        await dependencies.Commands.DidNotReceive().EnqueueAsync(Arg.Any<DietPlanUpdatedInAppNotificationCommand>());
        await dependencies.UnitOfWork.DidNotReceive().BeginTransactionAsync(Arg.Any<CancellationToken>());
    }

    [Test]
    public async Task MacroOnlyCreate_AllowsNoMeals()
    {
        var trainerId = Id<User>.New();
        var traineeId = Id<User>.New();
        var cancellationToken = new CancellationTokenSource().Token;
        var dependencies = new Dependencies();
        DietPlan? stagedPlan = null;
        dependencies.GrantAccess(trainerId, traineeId);
        dependencies.Plans.AddPlanAsync(Arg.Any<DietPlan>(), cancellationToken)
            .Returns(Task.CompletedTask)
            .AndDoes(call => stagedPlan = call.Arg<DietPlan>());
        dependencies.Plans.AddHistoryEntryAsync(Arg.Any<DietPlanHistory>(), cancellationToken).Returns(Task.CompletedTask);
        dependencies.UnitOfWork.SaveChangesAsync(cancellationToken).Returns(Task.FromResult(2));

        var result = await dependencies.CreateUseCase().ExecuteAsync(
            new CreateTraineeDietPlanCommand(
                trainerId,
                traineeId,
                new DietPlanUpsertData("Macros", new DateOnly(2026, 7, 23), null, 2200, null, null, null, null, false, [])),
            cancellationToken);

        result.IsSuccess.Should().BeTrue();
        stagedPlan!.Meals.Should().BeEmpty();
        result.Value.Meals.Should().BeEmpty();
        await dependencies.UnitOfWork.Received(1).SaveChangesAsync(cancellationToken);
        await dependencies.Commands.DidNotReceive().EnqueueAsync(Arg.Any<DietPlanUpdatedInAppNotificationCommand>());
        await dependencies.UnitOfWork.DidNotReceive().BeginTransactionAsync(Arg.Any<CancellationToken>());
    }

    [Test]
    public async Task NoMealAndNoMacro_ReturnsBadRequestAfterAccess()
    {
        var trainerId = Id<User>.New();
        var traineeId = Id<User>.New();
        var cancellationToken = new CancellationTokenSource().Token;
        var dependencies = new Dependencies();
        dependencies.GrantAccess(trainerId, traineeId);

        var result = await dependencies.CreateUseCase().ExecuteAsync(
            new CreateTraineeDietPlanCommand(
                trainerId,
                traineeId,
                new DietPlanUpsertData("Plan", new DateOnly(2026, 7, 23), null, null, null, null, null, null, false, [])),
            cancellationToken);

        result.Error.Should().BeOfType<BadRequestError>();
        result.Error.Message.Should().Be(Messages.FieldRequired);
        await dependencies.Access.Received(1).GetAccessDecisionAsync(trainerId, traineeId, cancellationToken);
        await AssertNoWritesAsync(dependencies);
    }

    [Test]
    public async Task InvalidNameStartOrMeal_ReturnsBadRequestAfterAccess()
    {
        var trainerId = Id<User>.New();
        var traineeId = Id<User>.New();
        var invalidBodies = new[]
        {
            ValidData(name: " "),
            ValidData(startDate: default(DateOnly)),
            ValidData(meals: [new DietMealInput(" ", 0, null, null, null, null, null)])
        };

        foreach (var data in invalidBodies)
        {
            var cancellationToken = new CancellationTokenSource().Token;
            var dependencies = new Dependencies();
            dependencies.GrantAccess(trainerId, traineeId);

            var result = await dependencies.CreateUseCase().ExecuteAsync(
                new CreateTraineeDietPlanCommand(trainerId, traineeId, data),
                cancellationToken);

            result.Error.Should().BeOfType<BadRequestError>();
            result.Error.Message.Should().Be(Messages.FieldRequired);
            await dependencies.Access.Received(1).GetAccessDecisionAsync(trainerId, traineeId, cancellationToken);
            await AssertNoWritesAsync(dependencies);
        }
    }

    [Test]
    public async Task EmptyTraineeId_ReturnsBadRequestWithoutAccess()
    {
        var trainerId = Id<User>.New();
        var dependencies = new Dependencies();

        var result = await dependencies.CreateUseCase().ExecuteAsync(
            new CreateTraineeDietPlanCommand(trainerId, Id<User>.Empty, ValidData()));

        result.Error.Should().BeOfType<BadRequestError>();
        result.Error.Message.Should().Be(Messages.UserIdRequired);
        await dependencies.Access.DidNotReceive().GetAccessDecisionAsync(
            Arg.Any<Id<User>>(),
            Arg.Any<Id<User>>(),
            Arg.Any<CancellationToken>());
        await AssertNoWritesAsync(dependencies);
    }

    [Test]
    public async Task NonTrainer_ReturnsForbiddenWithoutWrites()
    {
        var trainerId = Id<User>.New();
        var traineeId = Id<User>.New();
        var dependencies = new Dependencies();
        dependencies.Access.GetAccessDecisionAsync(trainerId, traineeId, Arg.Any<CancellationToken>())
            .Returns(new CoachingRelationshipAccessDecision(false, true));

        var result = await dependencies.CreateUseCase().ExecuteAsync(
            new CreateTraineeDietPlanCommand(trainerId, traineeId, ValidData()));

        result.Error.Should().BeOfType<TrainerRelationshipForbiddenError>();
        result.Error.Message.Should().Be(Messages.TrainerRoleRequired);
        await AssertNoWritesAsync(dependencies);
    }

    [Test]
    public async Task SaveFailure_PropagatesAndDoesNotEnqueue()
    {
        var trainerId = Id<User>.New();
        var traineeId = Id<User>.New();
        var cancellationToken = new CancellationTokenSource().Token;
        var operations = new List<string>();
        var dependencies = new Dependencies();
        dependencies.GrantAccess(trainerId, traineeId, operations);
        dependencies.Plans.AddPlanAsync(Arg.Any<DietPlan>(), cancellationToken)
            .Returns(Task.CompletedTask)
            .AndDoes(_ => operations.Add("stage plan"));
        dependencies.Plans.AddHistoryEntryAsync(Arg.Any<DietPlanHistory>(), cancellationToken)
            .Returns(Task.CompletedTask)
            .AndDoes(_ => operations.Add("stage history"));
        dependencies.UnitOfWork.SaveChangesAsync(cancellationToken)
            .Returns(Task.FromException<int>(new InvalidOperationException("save failed")))
            .AndDoes(_ => operations.Add("save"));

        Func<Task> act = () => dependencies.CreateUseCase().ExecuteAsync(
            new CreateTraineeDietPlanCommand(trainerId, traineeId, ValidData()),
            cancellationToken);

        await act.Should().ThrowAsync<InvalidOperationException>().WithMessage("save failed");
        operations.Should().Equal("access", "stage plan", "stage history", "save");
        await dependencies.UnitOfWork.Received(1).SaveChangesAsync(cancellationToken);
        await dependencies.Commands.DidNotReceive().EnqueueAsync(Arg.Any<DietPlanUpdatedInAppNotificationCommand>());
        await dependencies.UnitOfWork.DidNotReceive().BeginTransactionAsync(Arg.Any<CancellationToken>());
    }

    private static DietPlanUpsertData ValidData(
        string name = " Cut ",
        DateOnly? startDate = null,
        IEnumerable<DietMealInput>? meals = null,
        bool isActive = true)
        => new(
            name,
            startDate ?? new DateOnly(2026, 7, 23),
            null,
            2200,
            180m,
            220m,
            70m,
            " ",
            isActive,
            meals ??
            [
                new DietMealInput(" Dinner ", 2, " ", 900, 60m, 100m, 20m),
                new DietMealInput(" Breakfast ", -1, " eggs ", 600, 40m, 60m, 15m)
            ]);

    private static async Task AssertNoWritesAsync(Dependencies dependencies)
    {
        await dependencies.Plans.DidNotReceive().AddPlanAsync(
            Arg.Any<DietPlan>(),
            Arg.Any<CancellationToken>());
        await dependencies.Plans.DidNotReceive().AddHistoryEntryAsync(
            Arg.Any<DietPlanHistory>(),
            Arg.Any<CancellationToken>());
        await dependencies.UnitOfWork.DidNotReceive().SaveChangesAsync(Arg.Any<CancellationToken>());
        await dependencies.UnitOfWork.DidNotReceive().BeginTransactionAsync(Arg.Any<CancellationToken>());
        await dependencies.Commands.DidNotReceive().EnqueueAsync(Arg.Any<DietPlanUpdatedInAppNotificationCommand>());
    }

    private sealed class Dependencies
    {
        public ICoachingRelationshipAccessService Access { get; } = Substitute.For<ICoachingRelationshipAccessService>();
        public IDietPlanPersistence Plans { get; } = Substitute.For<IDietPlanPersistence>();
        public ICommandDispatcher Commands { get; } = Substitute.For<ICommandDispatcher>();
        public IUnitOfWork UnitOfWork { get; } = Substitute.For<IUnitOfWork>();
        private IMapper Mapper { get; } = CreateMapper();

        public void GrantAccess(Id<User> trainerId, Id<User> traineeId, List<string>? operations = null)
            => Access.GetAccessDecisionAsync(trainerId, traineeId, Arg.Any<CancellationToken>())
                .Returns(new CoachingRelationshipAccessDecision(true, true))
                .AndDoes(_ => operations?.Add("access"));

        public ICreateTraineeDietPlanUseCase CreateUseCase()
            => new CreateTraineeDietPlanUseCase(
                Access,
                Plans,
                Commands,
                UnitOfWork,
                Mapper,
                new DietPlanHistorySnapshotFactory(Mapper));

        private static IMapper CreateMapper()
        {
            var services = new ServiceCollection();
            services.AddApplicationMapping(typeof(IMappingProfile).Assembly);

            using var provider = services.BuildServiceProvider();
            return provider.GetRequiredService<IMapper>();
        }
    }
}
