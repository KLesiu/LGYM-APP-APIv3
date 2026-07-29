using LgymApi.Domain.Entities;
using LgymApi.Domain.ValueObjects;

namespace LgymApi.Application.Repositories;

internal interface IPlanDayExerciseRepository
{
    Task<List<PlanDayExercise>> GetByPlanDayIdsAsync(List<Id<PlanDay>> planDayIds, CancellationToken cancellationToken = default);
    Task<List<PlanDayExercise>> GetByPlanDayIdAsync(Id<PlanDay> planDayId, CancellationToken cancellationToken = default);
    Task AddRangeAsync(IEnumerable<PlanDayExercise> exercises, CancellationToken cancellationToken = default);
    Task RemoveByPlanDayIdAsync(Id<PlanDay> planDayId, CancellationToken cancellationToken = default);
}
