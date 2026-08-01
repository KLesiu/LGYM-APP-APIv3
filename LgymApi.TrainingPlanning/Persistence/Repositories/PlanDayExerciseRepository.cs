using LgymApi.Application.Repositories;
using LgymApi.Domain.Entities;
using LgymApi.Domain.ValueObjects;
using LgymApi.TrainingPlanning.Persistence;
using Microsoft.EntityFrameworkCore;

namespace LgymApi.Infrastructure.Repositories;

internal sealed class PlanDayExerciseRepository : IPlanDayExerciseRepository
{
    private readonly ITrainingPlanningPersistenceContext _context;

    public PlanDayExerciseRepository(ITrainingPlanningPersistenceContext context)
    {
        _context = context;
    }

    public Task<List<PlanDayExercise>> GetByPlanDayIdsAsync(List<Id<PlanDay>> planDayIds, CancellationToken cancellationToken = default)
        => _context.PlanDayExercises
            .AsNoTracking()
            .Where(exercise => planDayIds.Contains(exercise.PlanDayId))
            .OrderBy(exercise => exercise.PlanDayId)
            .ThenBy(exercise => exercise.Order)
            .ThenBy(exercise => exercise.Id)
            .ToListAsync(cancellationToken);

    public Task<List<PlanDayExercise>> GetByPlanDayIdAsync(Id<PlanDay> planDayId, CancellationToken cancellationToken = default)
        => _context.PlanDayExercises
            .AsNoTracking()
            .Where(exercise => exercise.PlanDayId == planDayId)
            .OrderBy(exercise => exercise.Order)
            .ThenBy(exercise => exercise.Id)
            .ToListAsync(cancellationToken);

    public async Task AddRangeAsync(IEnumerable<PlanDayExercise> exercises, CancellationToken cancellationToken = default)
    {
        var exercisesToAdd = exercises.ToList();
        if (exercisesToAdd.Count == 0)
        {
            return;
        }

        await _context.PlanDayExercises.AddRangeAsync(exercisesToAdd, cancellationToken);
        await NormalizeOrdersAsync(exercisesToAdd.Select(exercise => exercise.PlanDayId), cancellationToken);
    }

    public async Task RemoveByPlanDayIdAsync(Id<PlanDay> planDayId, CancellationToken cancellationToken = default)
    {
        var exercises = await _context.PlanDayExercises.Where(exercise => exercise.PlanDayId == planDayId).ToListAsync(cancellationToken);
        foreach (var exercise in exercises)
        {
            exercise.IsDeleted = true;
        }

        await NormalizeOrdersAsync([planDayId], cancellationToken);
    }

    private async Task NormalizeOrdersAsync(IEnumerable<Id<PlanDay>> planDayIds, CancellationToken cancellationToken)
    {
        var affectedPlanDayIds = planDayIds.Distinct().ToList();
        if (affectedPlanDayIds.Count == 0)
        {
            return;
        }

        var orderedExercises = await _context.PlanDayExercises
            .Where(exercise => affectedPlanDayIds.Contains(exercise.PlanDayId))
            .OrderBy(exercise => exercise.PlanDayId)
            .ThenBy(exercise => exercise.Order)
            .ThenBy(exercise => exercise.Id)
            .ToListAsync(cancellationToken);

        Id<PlanDay>? currentPlanDayId = null;
        var nextOrder = 0;

        foreach (var exercise in orderedExercises)
        {
            if (currentPlanDayId != exercise.PlanDayId)
            {
                currentPlanDayId = exercise.PlanDayId;
                nextOrder = 0;
            }

            if (exercise.Order != nextOrder)
            {
                exercise.Order = nextOrder;
            }

            nextOrder++;
        }
    }
}
