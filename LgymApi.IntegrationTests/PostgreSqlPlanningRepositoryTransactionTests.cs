using FluentAssertions;
using LgymApi.Application.Repositories;
using LgymApi.Application.TrainingPlanning;
using LgymApi.Application.TrainingPlanning.Contracts.PlanDay;
using LgymApi.Application.TrainingPlanning.Plan.ActivePlanPointer;
using LgymApi.Application.TrainingPlanning.Plan.CopyPlan;
using LgymApi.Application.TrainingPlanning.Plan.DeletePlan;
using LgymApi.Application.TrainingPlanning.Plan.UpdatePlan;
using LgymApi.Domain.Entities;
using LgymApi.Domain.Enums;
using LgymApi.Domain.ValueObjects;
using LgymApi.Identity.Contracts;
using LgymApi.Infrastructure.Data;
using LgymApi.Infrastructure.UnitOfWork;
using LgymApi.TrainingPlanning.Contracts;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace LgymApi.IntegrationTests;

[TestFixture]
[Category("PostgreSql")]
internal sealed class PostgreSqlPlanningRepositoryTransactionTests : PostgreSqlIntegrationTestBase
{
    [Test]
    public async Task UpdatePlanAsync_StagesTheRepositoryWriteUntilOneCommit()
    {
        var user = await SeedUserAsync($"plan-update-{Id<User>.New():N}", $"plan-update-{Id<User>.New():N}@example.com");
        var plan = await SeedPlanAsync(user.Id, "Before update", isActive: true);
        var stagedWriteWasInvisible = false;

        await using (var serviceScope = Factory.Services.CreateAsyncScope())
        {
            var database = serviceScope.ServiceProvider.GetRequiredService<AppDbContext>();
            var unitOfWork = new ObservedUnitOfWork(new EfUnitOfWork(database), async () =>
            {
                stagedWriteWasInvisible = await ReadPlanNameAsync(plan.Id) == "Before update";
            });
            var useCase = new UpdatePlanUseCase(
                serviceScope.ServiceProvider.GetRequiredService<IPlanRepository>(),
                unitOfWork);

            var result = await useCase.ExecuteAsync(new UpdatePlanCommand(
                user.Id.Rebind<AccountReference>(),
                user.Id.Rebind<AccountReference>(),
                plan.Id.Rebind<PlanReference>(),
                "After update"));

            result.IsSuccess.Should().BeTrue();
            unitOfWork.SaveChangesCalls.Should().Be(1);
            unitOfWork.BeginTransactionCalls.Should().Be(0);
        }

        stagedWriteWasInvisible.Should().BeTrue();
        (await ReadPlanNameAsync(plan.Id)).Should().Be("After update");
    }

    [Test]
    public async Task DeletePlanAsync_CommitsPlanDayDeleteAndFallbackActivationInOneTransaction()
    {
        var user = await SeedUserAsync($"plan-delete-{Id<User>.New():N}", $"plan-delete-{Id<User>.New():N}@example.com");
        var fallbackPlan = await SeedPlanAsync(user.Id, "Fallback", isActive: false, createdAt: DateTimeOffset.UtcNow.AddMinutes(-1));
        var deletedPlan = await SeedPlanAsync(user.Id, "Delete", isActive: true, createdAt: DateTimeOffset.UtcNow);
        var planDayId = await SeedPlanDayAsync(deletedPlan.Id);
        var stagedWritesWereInvisible = false;

        await using (var serviceScope = Factory.Services.CreateAsyncScope())
        {
            var database = serviceScope.ServiceProvider.GetRequiredService<AppDbContext>();
            var unitOfWork = new ObservedUnitOfWork(new EfUnitOfWork(database), async () =>
            {
                var state = await ReadDeleteStateAsync(deletedPlan.Id, fallbackPlan.Id, planDayId);
                stagedWritesWereInvisible = !state.PlanDeleted && !state.PlanDayDeleted && !state.FallbackActive;
            });
            var useCase = new DeletePlanUseCase(
                serviceScope.ServiceProvider.GetRequiredService<IPlanRepository>(),
                serviceScope.ServiceProvider.GetRequiredService<IPlanDayRepository>(),
                serviceScope.ServiceProvider.GetRequiredService<IActivePlanPointerStore>(),
                unitOfWork);

            var result = await useCase.ExecuteAsync(new DeletePlanCommand(
                user.Id.Rebind<AccountReference>(),
                deletedPlan.Id.Rebind<PlanReference>()));

            result.IsSuccess.Should().BeTrue();
            unitOfWork.SaveChangesCalls.Should().Be(1);
            unitOfWork.BeginTransactionCalls.Should().Be(1);
            unitOfWork.CommitCalls.Should().Be(1);
            unitOfWork.RollbackCalls.Should().Be(0);
        }

        stagedWritesWereInvisible.Should().BeTrue();
        var persistedState = await ReadDeleteStateAsync(deletedPlan.Id, fallbackPlan.Id, planDayId);
        persistedState.PlanDeleted.Should().BeTrue();
        persistedState.PlanDayDeleted.Should().BeTrue();
        persistedState.FallbackActive.Should().BeTrue();
    }

    [Test]
    public async Task CopyPlanAsync_WithWorkoutAdapter_CommitsTheWholeCloneBeforeDispatch()
    {
        var graph = await SeedCloneGraphAsync();
        var durableCloneWasObservedByDispatch = false;

        await using (var serviceScope = Factory.Services.CreateAsyncScope())
        {
            var database = serviceScope.ServiceProvider.GetRequiredService<AppDbContext>();
            var dispatcher = new RecordingDispatcher(async () =>
            {
                var counts = await ReadCloneCountsAsync(graph.TargetUserId);
                durableCloneWasObservedByDispatch = counts == new CloneCounts(1, 1, 1, 1);
            });
            var unitOfWork = new ObservedUnitOfWork(new EfUnitOfWork(database, dispatcher));
            var useCase = new CopyPlanUseCase(
                serviceScope.ServiceProvider.GetRequiredService<IPlanRepository>(),
                serviceScope.ServiceProvider.GetRequiredService<IPlanExerciseClonePort>(),
                unitOfWork);

            var result = await useCase.ExecuteAsync(new CopyPlanCommand(
                graph.TargetUserId.Rebind<AccountReference>(),
                graph.ShareCode));

            result.IsSuccess.Should().BeTrue();
            unitOfWork.SaveChangesCalls.Should().Be(1);
            unitOfWork.BeginTransactionCalls.Should().Be(1);
            unitOfWork.CommitCalls.Should().Be(1);
            unitOfWork.RollbackCalls.Should().Be(0);
            dispatcher.CallCount.Should().Be(1);
        }

        durableCloneWasObservedByDispatch.Should().BeTrue();
        (await ReadCloneCountsAsync(graph.TargetUserId)).Should().Be(new CloneCounts(1, 1, 1, 1));
    }

    [Test]
    public async Task CopyPlanAsync_WhenWorkoutCloneFlushesThenFails_RollsBackEveryModuleWriteWithoutDispatch()
    {
        var graph = await SeedCloneGraphAsync();

        await using (var serviceScope = Factory.Services.CreateAsyncScope())
        {
            var database = serviceScope.ServiceProvider.GetRequiredService<AppDbContext>();
            var dispatcher = new RecordingDispatcher();
            var unitOfWork = new ObservedUnitOfWork(new EfUnitOfWork(database, dispatcher));
            var clonePort = new FlushThenThrowClonePort(
                serviceScope.ServiceProvider.GetRequiredService<IPlanExerciseClonePort>(),
                database);
            var useCase = new CopyPlanUseCase(
                serviceScope.ServiceProvider.GetRequiredService<IPlanRepository>(),
                clonePort,
                unitOfWork);

            var action = () => useCase.ExecuteAsync(new CopyPlanCommand(
                graph.TargetUserId.Rebind<AccountReference>(),
                graph.ShareCode));

            await action.Should().ThrowAsync<InvalidOperationException>();
            clonePort.FlushCalls.Should().Be(1);
            unitOfWork.SaveChangesCalls.Should().Be(0);
            unitOfWork.BeginTransactionCalls.Should().Be(1);
            unitOfWork.CommitCalls.Should().Be(0);
            unitOfWork.RollbackCalls.Should().Be(1);
            dispatcher.CallCount.Should().Be(0);
        }

        (await ReadCloneCountsAsync(graph.TargetUserId)).Should().Be(new CloneCounts(0, 0, 0, 0));
    }

    private async Task<Plan> SeedPlanAsync(
        Id<User> userId,
        string name,
        bool isActive,
        DateTimeOffset? createdAt = null)
    {
        var plan = new Plan
        {
            Id = Id<Plan>.New(),
            UserId = userId,
            Name = name,
            IsActive = isActive,
            CreatedAt = createdAt ?? DateTimeOffset.UtcNow
        };
        await using var scope = Factory.Services.CreateAsyncScope();
        var database = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        database.Plans.Add(plan);
        await database.SaveChangesAsync();
        return plan;
    }

    private async Task<Id<PlanDay>> SeedPlanDayAsync(Id<Plan> planId)
    {
        var planDay = new PlanDay { Id = Id<PlanDay>.New(), PlanId = planId, Name = "Day" };
        await using var scope = Factory.Services.CreateAsyncScope();
        var database = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        database.PlanDays.Add(planDay);
        await database.SaveChangesAsync();
        return planDay.Id;
    }

    private async Task<CloneGraph> SeedCloneGraphAsync()
    {
        var sourceUser = await SeedUserAsync($"clone-source-{Id<User>.New():N}", $"clone-source-{Id<User>.New():N}@example.com");
        var targetUser = await SeedUserAsync($"clone-target-{Id<User>.New():N}", $"clone-target-{Id<User>.New():N}@example.com");
        var shareCode = $"{Id<Plan>.New():N}"[..10];
        var exercise = new Exercise
        {
            Id = Id<Exercise>.New(),
            UserId = sourceUser.Id,
            Name = "Custom exercise",
            BodyPart = BodyParts.Chest
        };
        var plan = new Plan
        {
            Id = Id<Plan>.New(),
            UserId = sourceUser.Id,
            Name = "Source plan",
            IsActive = false,
            ShareCode = shareCode
        };
        var planDay = new PlanDay { Id = Id<PlanDay>.New(), PlanId = plan.Id, Name = "Source day" };
        var planDayExercise = new PlanDayExercise
        {
            Id = Id<PlanDayExercise>.New(),
            PlanDayId = planDay.Id,
            ExerciseId = exercise.Id,
            Order = 0,
            Series = 3,
            Reps = "8"
        };

        await using var scope = Factory.Services.CreateAsyncScope();
        var database = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        database.Exercises.Add(exercise);
        database.Plans.Add(plan);
        database.PlanDays.Add(planDay);
        database.PlanDayExercises.Add(planDayExercise);
        await database.SaveChangesAsync();
        return new CloneGraph(targetUser.Id, shareCode);
    }

    private async Task<string> ReadPlanNameAsync(Id<Plan> planId)
    {
        await using var scope = Factory.Services.CreateAsyncScope();
        var database = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        return await database.Plans.AsNoTracking()
            .Where(plan => plan.Id == planId)
            .Select(plan => plan.Name)
            .SingleAsync();
    }

    private async Task<DeleteState> ReadDeleteStateAsync(
        Id<Plan> deletedPlanId,
        Id<Plan> fallbackPlanId,
        Id<PlanDay> planDayId)
    {
        await using var scope = Factory.Services.CreateAsyncScope();
        var database = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var deletedPlan = await database.Plans.IgnoreQueryFilters().AsNoTracking()
            .SingleAsync(plan => plan.Id == deletedPlanId);
        var fallbackPlan = await database.Plans.IgnoreQueryFilters().AsNoTracking()
            .SingleAsync(plan => plan.Id == fallbackPlanId);
        var planDay = await database.PlanDays.IgnoreQueryFilters().AsNoTracking()
            .SingleAsync(candidate => candidate.Id == planDayId);
        return new DeleteState(deletedPlan.IsDeleted, planDay.IsDeleted, fallbackPlan.IsActive);
    }

    private async Task<CloneCounts> ReadCloneCountsAsync(Id<User> targetUserId)
    {
        await using var scope = Factory.Services.CreateAsyncScope();
        var database = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var planIds = await database.Plans.AsNoTracking()
            .Where(plan => plan.UserId == targetUserId)
            .Select(plan => plan.Id)
            .ToListAsync();
        var planDayIds = await database.PlanDays.AsNoTracking()
            .Where(planDay => planIds.Contains(planDay.PlanId))
            .Select(planDay => planDay.Id)
            .ToListAsync();
        return new CloneCounts(
            await database.Exercises.CountAsync(exercise => exercise.UserId == targetUserId),
            planIds.Count,
            planDayIds.Count,
            await database.PlanDayExercises.CountAsync(exercise => planDayIds.Contains(exercise.PlanDayId)));
    }

    private sealed record CloneGraph(Id<User> TargetUserId, string ShareCode);
    private sealed record CloneCounts(int Exercises, int Plans, int PlanDays, int PlanDayExercises);
    private sealed record DeleteState(bool PlanDeleted, bool PlanDayDeleted, bool FallbackActive);

    private sealed class RecordingDispatcher(Func<Task>? onDispatch = null) : ICommittedIntentDispatcher
    {
        public int CallCount { get; private set; }

        public async Task DispatchCommittedIntentsAsync(CancellationToken cancellationToken = default)
        {
            CallCount++;
            if (onDispatch is not null)
            {
                await onDispatch();
            }
        }
    }

    private sealed class FlushThenThrowClonePort(
        IPlanExerciseClonePort inner,
        AppDbContext database) : IPlanExerciseClonePort
    {
        public int FlushCalls { get; private set; }

        public async Task<IReadOnlyDictionary<Id<PlanExerciseReference>, Id<PlanExerciseReference>>> StageClonesAsync(
            Id<AccountReference> targetAccountId,
            IReadOnlyCollection<Id<PlanExerciseReference>> exerciseIds,
            CancellationToken cancellationToken = default)
        {
            await inner.StageClonesAsync(targetAccountId, exerciseIds, cancellationToken);
            FlushCalls++;
            await database.SaveChangesAsync(cancellationToken);
            throw new InvalidOperationException("Forced failure after the Workout clone was flushed.");
        }
    }
}
