using FluentAssertions;
using LgymApi.Application.Coaching.Contracts.Access;
using LgymApi.Application.BuildingBlocks.Errors;
using LgymApi.Application.Coaching.Errors;
using LgymApi.Application.Mapping;
using LgymApi.Application.Mapping.Core;
using LgymApi.Application.Nutrition.Contracts.BackgroundCommands;
using LgymApi.Application.Nutrition.DietPlans;
using LgymApi.Application.Nutrition.DietPlans.Models;
using LgymApi.Application.Nutrition.DietPlans.UpdateTraineePlan;
using LgymApi.Application.Nutrition.Persistence;
using LgymApi.Application.Platform.Contracts.BackgroundCommands;
using LgymApi.Application.Repositories;
using LgymApi.Domain.Entities;
using LgymApi.Domain.ValueObjects;
using LgymApi.Resources;
using Microsoft.Extensions.DependencyInjection;
using NSubstitute;
using NUnit.Framework;

namespace LgymApi.UnitTests.Nutrition.DietPlans.UpdateTraineePlan;

[TestFixture]
public sealed class UpdateTraineeDietPlanUseCaseTests
{
    [Test]
    public async Task ExecuteAsync_WhenActive_StagesUpdatedSnapshotSavesThenNotifies()
    {
        var trainerId = Id<User>.New();
        var traineeId = Id<User>.New();
        var plan = ExistingPlan(trainerId, traineeId, Id<DietPlan>.New());
        var command = ValidCommand(trainerId, traineeId, plan.Id);
        var dependencies = new Dependencies();
        var operations = new List<string>();
        DietPlanHistory? history = null;
        DietPlanUpdatedInAppNotificationCommand? notification = null;

        dependencies.Access.GetAccessDecisionAsync(trainerId, traineeId, Arg.Any<CancellationToken>())
            .Returns(new CoachingRelationshipAccessDecision(true, true))
            .AndDoes(_ => operations.Add("access"));
        dependencies.Plans.FindTrackedPlanByIdAsync(plan.Id, Arg.Any<CancellationToken>())
            .Returns(plan)
            .AndDoes(_ => operations.Add("tracked"));
        dependencies.Plans.AddHistoryEntryAsync(Arg.Any<DietPlanHistory>(), Arg.Any<CancellationToken>())
            .Returns(Task.CompletedTask)
            .AndDoes(call =>
            {
                history = call.Arg<DietPlanHistory>();
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
                operations.Add("notify");
            });

        var result = await dependencies.CreateUseCase().ExecuteAsync(command);

        result.IsSuccess.Should().BeTrue();
        plan.Name.Should().Be("Nutrition Plan");
        history.Should().NotBeNull();
        history!.ChangeType.Should().Be("Updated");
        history.DietPlanId.Should().Be(plan.Id);
        history.ChangedByUserId.Should().Be(trainerId);
        notification.Should().NotBeNull();
        notification!.DietPlanId.Should().Be(plan.Id);
        notification.TraineeId.Should().Be(traineeId);
        notification.TrainerId.Should().Be(trainerId);
        notification.DietPlanName.Should().Be("Nutrition Plan");
        operations.Should().Equal("access", "tracked", "history", "save", "notify");
        await AssertNoTransactionAsync(dependencies);
    }

    [Test]
    public async Task ExecuteAsync_WhenInactive_SavesWithoutNotification()
    {
        var trainerId = Id<User>.New();
        var traineeId = Id<User>.New();
        var plan = ExistingPlan(trainerId, traineeId, Id<DietPlan>.New());
        var command = ValidCommand(trainerId, traineeId, plan.Id, isActive: false);
        var dependencies = new Dependencies();
        ConfigureSuccessfulUpdate(dependencies, command, plan);

        var result = await dependencies.CreateUseCase().ExecuteAsync(command);

        result.IsSuccess.Should().BeTrue();
        plan.IsActive.Should().BeFalse();
        await dependencies.UnitOfWork.Received(1).SaveChangesAsync(Arg.Any<CancellationToken>());
        await dependencies.Commands.DidNotReceive().EnqueueAsync(Arg.Any<DietPlanUpdatedInAppNotificationCommand>());
        await AssertNoTransactionAsync(dependencies);
    }

    [Test]
    public async Task ExecuteAsync_ReplacesMealsWithNewIdsInNormalizedOrder()
    {
        var trainerId = Id<User>.New();
        var traineeId = Id<User>.New();
        var plan = ExistingPlan(trainerId, traineeId, Id<DietPlan>.New());
        var oldMealIds = plan.Meals.Select(meal => meal.Id).ToArray();
        var command = ValidCommand(
            trainerId,
            traineeId,
            plan.Id,
            meals:
            [
                new DietMealInput(" Dinner ", 2, " rice ", 900, 55m, 100.5m, 20.25m),
                new DietMealInput(" Breakfast ", 1, null, 500, 30.5m, 60m, 10m)
            ]);
        var dependencies = new Dependencies();
        ConfigureSuccessfulUpdate(dependencies, command, plan);

        var result = await dependencies.CreateUseCase().ExecuteAsync(command);

        result.IsSuccess.Should().BeTrue();
        plan.Meals.Select(meal => meal.Order).Should().Equal(1, 2);
        plan.Meals.Select(meal => meal.Name).Should().Equal("Breakfast", "Dinner");
        plan.Meals.Should().OnlyContain(meal => !meal.Id.IsEmpty);
        plan.Meals.Select(meal => meal.Id).Should().NotIntersectWith(oldMealIds);
    }

    [Test]
    public async Task ExecuteAsync_WhenBodyIsInvalid_ReturnsBadRequestBeforeCoaching()
    {
        var dependencies = new Dependencies();
        var command = new UpdateTraineeDietPlanCommand(
            Id<User>.New(),
            Id<User>.New(),
            Id<DietPlan>.New(),
            new DietPlanUpsertData(" ", default, null, null, null, null, null, null, true, []));

        var result = await dependencies.CreateUseCase().ExecuteAsync(command);

        result.Error.Should().BeOfType<BadRequestError>();
        result.Error.Message.Should().Be(Messages.FieldRequired);
        await dependencies.Access.DidNotReceive().GetAccessDecisionAsync(
            Arg.Any<Id<User>>(),
            Arg.Any<Id<User>>(),
            Arg.Any<CancellationToken>());
        await AssertNoWritesAsync(dependencies);
    }

    [Test]
    public async Task ExecuteAsync_WhenCallerIsNotTrainer_ReturnsForbiddenWithoutPersistence()
    {
        var trainerId = Id<User>.New();
        var traineeId = Id<User>.New();
        var dependencies = new Dependencies();
        var command = ValidCommand(trainerId, traineeId, Id<DietPlan>.New());
        dependencies.Access.GetAccessDecisionAsync(trainerId, traineeId, Arg.Any<CancellationToken>())
            .Returns(new CoachingRelationshipAccessDecision(false, true));

        var result = await dependencies.CreateUseCase().ExecuteAsync(command);

        result.Error.Should().BeOfType<TrainerRelationshipForbiddenError>();
        result.Error.Message.Should().Be(Messages.TrainerRoleRequired);
        await dependencies.Plans.DidNotReceive().FindTrackedPlanByIdAsync(
            Arg.Any<Id<DietPlan>>(),
            Arg.Any<CancellationToken>());
        await AssertNoWritesAsync(dependencies);
    }

    [Test]
    public async Task ExecuteAsync_WhenPlanIsEmptyForeignOrDeleted_ReturnsExpectedFailureWithoutWriting()
    {
        var trainerId = Id<User>.New();
        var traineeId = Id<User>.New();
        var planId = Id<DietPlan>.New();
        var cases = new[]
        {
            new PlanFailureCase(Id<DietPlan>.Empty, null, typeof(BadRequestError), Messages.FieldRequired),
            new PlanFailureCase(planId, ExistingPlan(Id<User>.New(), traineeId, planId), typeof(NotFoundError), Messages.DidntFind),
            new PlanFailureCase(planId, ExistingPlan(trainerId, traineeId, planId, isDeleted: true), typeof(NotFoundError), Messages.DidntFind)
        };

        foreach (var failureCase in cases)
        {
            var dependencies = new Dependencies();
            var command = ValidCommand(trainerId, traineeId, failureCase.PlanId);
            dependencies.Access.GetAccessDecisionAsync(trainerId, traineeId, Arg.Any<CancellationToken>())
                .Returns(new CoachingRelationshipAccessDecision(true, true));
            if (failureCase.Plan is not null)
            {
                dependencies.Plans.FindTrackedPlanByIdAsync(failureCase.PlanId, Arg.Any<CancellationToken>())
                    .Returns(failureCase.Plan);
            }

            var result = await dependencies.CreateUseCase().ExecuteAsync(command);

            result.Error.GetType().Should().Be(failureCase.ErrorType);
            result.Error.Message.Should().Be(failureCase.Message);
            if (failureCase.PlanId.IsEmpty)
            {
                await dependencies.Plans.DidNotReceive().FindTrackedPlanByIdAsync(
                    Arg.Any<Id<DietPlan>>(),
                    Arg.Any<CancellationToken>());
            }

            await AssertNoWritesAsync(dependencies);
        }
    }

    [Test]
    public async Task ExecuteAsync_StagesTheExactT2UpdatedSnapshot()
    {
        var trainerId = ParseId<User>("00000000-0000-0000-0000-000000000101");
        var traineeId = ParseId<User>("00000000-0000-0000-0000-000000000102");
        var plan = ExistingPlan(
            trainerId,
            traineeId,
            ParseId<DietPlan>("00000000-0000-0000-0000-000000000201"),
            createdAt: new DateTimeOffset(2026, 6, 1, 8, 9, 10, TimeSpan.FromHours(2)),
            updatedAt: new DateTimeOffset(2026, 6, 2, 9, 10, 11, TimeSpan.FromHours(2)));
        var command = new UpdateTraineeDietPlanCommand(
            trainerId,
            traineeId,
            plan.Id,
            new DietPlanUpsertData(
                " Lean Bulk ",
                new DateOnly(2026, 6, 1),
                null,
                2_800,
                180.5m,
                null,
                70.25m,
                null,
                true,
                [
                    new DietMealInput(" Dinner ", 2, " Rice ", 900, 55m, 100.5m, 20.25m),
                    new DietMealInput(" Breakfast ", 1, null, 500, 30.5m, 60m, 10m)
                ]));
        var dependencies = new Dependencies();
        DietPlanHistory? history = null;
        ConfigureSuccessfulUpdate(dependencies, command, plan, stagedHistory => history = stagedHistory);

        var result = await dependencies.CreateUseCase().ExecuteAsync(command);

        result.IsSuccess.Should().BeTrue();
        history.Should().NotBeNull();
        history!.SnapshotJson.Should().Be(UpdatedT2Golden(plan));
    }

    [Test]
    public async Task ExecuteAsync_WhenSaveFails_DoesNotNotify()
    {
        var trainerId = Id<User>.New();
        var traineeId = Id<User>.New();
        var plan = ExistingPlan(trainerId, traineeId, Id<DietPlan>.New());
        var command = ValidCommand(trainerId, traineeId, plan.Id);
        var dependencies = new Dependencies();
        ConfigureSuccessfulUpdate(dependencies, command, plan);
        dependencies.UnitOfWork.SaveChangesAsync(Arg.Any<CancellationToken>())
            .Returns(Task.FromException<int>(new InvalidOperationException("save failed")));

        await FluentActions.Awaiting(() => dependencies.CreateUseCase().ExecuteAsync(command))
            .Should().ThrowAsync<InvalidOperationException>()
            .WithMessage("save failed");

        await dependencies.UnitOfWork.Received(1).SaveChangesAsync(Arg.Any<CancellationToken>());
        await dependencies.Commands.DidNotReceive().EnqueueAsync(Arg.Any<DietPlanUpdatedInAppNotificationCommand>());
        await AssertNoTransactionAsync(dependencies);
    }

    private static void ConfigureSuccessfulUpdate(
        Dependencies dependencies,
        UpdateTraineeDietPlanCommand command,
        DietPlan plan,
        Action<DietPlanHistory>? stagedHistory = null)
    {
        dependencies.Access.GetAccessDecisionAsync(command.TrainerId, command.TraineeId, Arg.Any<CancellationToken>())
            .Returns(new CoachingRelationshipAccessDecision(true, true));
        dependencies.Plans.FindTrackedPlanByIdAsync(command.DietPlanId, Arg.Any<CancellationToken>())
            .Returns(plan);
        dependencies.Plans.AddHistoryEntryAsync(Arg.Any<DietPlanHistory>(), Arg.Any<CancellationToken>())
            .Returns(Task.CompletedTask)
            .AndDoes(call => stagedHistory?.Invoke(call.Arg<DietPlanHistory>()));
        dependencies.UnitOfWork.SaveChangesAsync(Arg.Any<CancellationToken>()).Returns(1);
        dependencies.Commands.EnqueueAsync(Arg.Any<DietPlanUpdatedInAppNotificationCommand>()).Returns(Task.CompletedTask);
    }

    private static async Task AssertNoWritesAsync(Dependencies dependencies)
    {
        await dependencies.Plans.DidNotReceive().AddHistoryEntryAsync(
            Arg.Any<DietPlanHistory>(),
            Arg.Any<CancellationToken>());
        await dependencies.UnitOfWork.DidNotReceive().SaveChangesAsync(Arg.Any<CancellationToken>());
        await dependencies.Commands.DidNotReceive().EnqueueAsync(Arg.Any<DietPlanUpdatedInAppNotificationCommand>());
        await AssertNoTransactionAsync(dependencies);
    }

    private static async Task AssertNoTransactionAsync(Dependencies dependencies)
        => await dependencies.UnitOfWork.DidNotReceive().BeginTransactionAsync(Arg.Any<CancellationToken>());

    private static UpdateTraineeDietPlanCommand ValidCommand(
        Id<User> trainerId,
        Id<User> traineeId,
        Id<DietPlan> planId,
        bool isActive = true,
        IReadOnlyList<DietMealInput>? meals = null)
        => new(trainerId, traineeId, planId, new DietPlanUpsertData(
            " Nutrition Plan ",
            new DateOnly(2026, 7, 23),
            null,
            2_500,
            170m,
            250m,
            70m,
            " notes ",
            isActive,
            meals ?? [new DietMealInput(" Breakfast ", 0, null, 500, 30m, 60m, 10m)]));

    private static DietPlan ExistingPlan(
        Id<User> trainerId,
        Id<User> traineeId,
        Id<DietPlan> planId,
        bool isDeleted = false,
        DateTimeOffset? createdAt = null,
        DateTimeOffset? updatedAt = null)
        => new()
        {
            Id = planId,
            TrainerId = trainerId,
            TraineeId = traineeId,
            Name = "Old plan",
            StartDate = new DateOnly(2026, 1, 1),
            IsActive = true,
            IsDeleted = isDeleted,
            CreatedAt = createdAt ?? DateTimeOffset.UtcNow,
            UpdatedAt = updatedAt ?? DateTimeOffset.UtcNow,
            Meals =
            [
                new DietMeal
                {
                    Id = Id<DietMeal>.New(),
                    Name = "Old meal",
                    Order = 0
                }
            ]
        };

    private static Id<TEntity> ParseId<TEntity>(string value)
        where TEntity : class
    {
        Id<TEntity>.TryParse(value, out var id).Should().BeTrue();
        return id;
    }

    private static string UpdatedT2Golden(DietPlan plan)
    {
        var breakfast = plan.Meals.Single(meal => meal.Order == 1);
        var dinner = plan.Meals.Single(meal => meal.Order == 2);

        return """
{"Id":{"Value":"00000000-0000-0000-0000-000000000201","IsEmpty":false},"TrainerId":{"Value":"00000000-0000-0000-0000-000000000101","IsEmpty":false},"TraineeId":{"Value":"00000000-0000-0000-0000-000000000102","IsEmpty":false},"Name":"Lean Bulk","StartDate":"2026-06-01","EndDate":null,"EstimatedCalories":2800,"ProteinGrams":180.5,"CarbsGrams":null,"FatGrams":70.25,"Notes":null,"IsActive":true,"CreatedAt":"2026-06-01T08:09:10+02:00","UpdatedAt":"2026-06-02T09:10:11+02:00","Meals":[{"Id":{"Value":"BREAKFAST_ID","IsEmpty":false},"Name":"Breakfast","Order":1,"Description":null,"EstimatedCalories":500,"ProteinGrams":30.5,"CarbsGrams":60,"FatGrams":10},{"Id":{"Value":"DINNER_ID","IsEmpty":false},"Name":"Dinner","Order":2,"Description":"Rice","EstimatedCalories":900,"ProteinGrams":55,"CarbsGrams":100.5,"FatGrams":20.25}]}
"""
            .Replace("BREAKFAST_ID", breakfast.Id.ToString(), StringComparison.Ordinal)
            .Replace("DINNER_ID", dinner.Id.ToString(), StringComparison.Ordinal);
    }

    private sealed record PlanFailureCase(
        Id<DietPlan> PlanId,
        DietPlan? Plan,
        Type ErrorType,
        string Message);

    private sealed class Dependencies
    {
        public ICoachingRelationshipAccessService Access { get; } = Substitute.For<ICoachingRelationshipAccessService>();
        public IDietPlanPersistence Plans { get; } = Substitute.For<IDietPlanPersistence>();
        public ICommandDispatcher Commands { get; } = Substitute.For<ICommandDispatcher>();
        public IUnitOfWork UnitOfWork { get; } = Substitute.For<IUnitOfWork>();
        public IMapper Mapper { get; } = CreateMapper();

        public UpdateTraineeDietPlanUseCase CreateUseCase()
            => new(Access, Plans, new DietPlanHistorySnapshotFactory(Mapper), Commands, UnitOfWork, Mapper);
    }

    private static IMapper CreateMapper()
    {
        var services = new ServiceCollection();
        services.AddApplicationMapping(typeof(IMappingProfile).Assembly);

        using var provider = services.BuildServiceProvider();
        return provider.GetRequiredService<IMapper>();
    }
}
