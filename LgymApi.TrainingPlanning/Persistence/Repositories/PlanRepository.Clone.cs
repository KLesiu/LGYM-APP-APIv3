using LgymApi.Domain.Entities;
using LgymApi.Domain.ValueObjects;
using LgymApi.TrainingPlanning.Contracts;
using Microsoft.EntityFrameworkCore;

namespace LgymApi.Infrastructure.Repositories;

internal sealed partial class PlanRepository
{
    private async Task<Plan> ClonePlanGraphAsync(
        Plan planToCopy,
        Id<User> userId,
        IReadOnlyDictionary<Id<PlanExerciseReference>, Id<PlanExerciseReference>> exerciseIdMap,
        bool isActive,
        CancellationToken cancellationToken)
    {
        var planDaysToCopy = await LoadPlanDaysAsync(planToCopy.Id, cancellationToken);
        var newPlan = await CreateNewPlanAsync(planToCopy, userId, isActive, cancellationToken);
        foreach (var planDay in planDaysToCopy)
        {
            await ProcessPlanDayAsync(planDay, newPlan.Id, exerciseIdMap, cancellationToken);
        }

        return newPlan;
    }

    private Task<List<PlanDay>> LoadPlanDaysAsync(Id<Plan> planId, CancellationToken cancellationToken)
        => _context.PlanDays
            .Include(planDay => planDay.Exercises)
            .Where(planDay => planDay.PlanId == planId && !planDay.IsDeleted)
            .ToListAsync(cancellationToken);

    private async Task<Plan> CreateNewPlanAsync(Plan sourcePlan, Id<User> userId, bool isActive, CancellationToken cancellationToken)
    {
        var newPlan = new Plan { Id = Id<Plan>.New(), UserId = userId, Name = sourcePlan.Name, IsActive = isActive, IsDeleted = false };
        await _context.Plans.AddAsync(newPlan, cancellationToken);
        return newPlan;
    }

    private async Task ProcessPlanDayAsync(
        PlanDay sourcePlanDay,
        Id<Plan> newPlanId,
        IReadOnlyDictionary<Id<PlanExerciseReference>, Id<PlanExerciseReference>> exerciseIdMap,
        CancellationToken cancellationToken)
    {
        var newPlanDay = new PlanDay { Id = Id<PlanDay>.New(), PlanId = newPlanId, Name = sourcePlanDay.Name, IsDeleted = false };
        await _context.PlanDays.AddAsync(newPlanDay, cancellationToken);

        foreach (var planDayExercise in sourcePlanDay.Exercises.OrderBy(exercise => exercise.Order).ThenBy(exercise => exercise.Id))
        {
            if (exerciseIdMap.TryGetValue(planDayExercise.ExerciseId.Rebind<PlanExerciseReference>(), out var exerciseId))
            {
                await AddCopiedPlanDayExerciseAsync(newPlanDay.Id, planDayExercise, exerciseId.Rebind<Exercise>(), cancellationToken);
            }
        }
    }

    private Task AddCopiedPlanDayExerciseAsync(Id<PlanDay> newPlanDayId, PlanDayExercise sourcePlanDayExercise, Id<Exercise> exerciseId, CancellationToken cancellationToken)
        => _context.PlanDayExercises.AddAsync(new PlanDayExercise
        {
            Id = Id<PlanDayExercise>.New(),
            PlanDayId = newPlanDayId,
            ExerciseId = exerciseId,
            Order = sourcePlanDayExercise.Order,
            Series = sourcePlanDayExercise.Series,
            Reps = sourcePlanDayExercise.Reps
        }, cancellationToken).AsTask();
}
