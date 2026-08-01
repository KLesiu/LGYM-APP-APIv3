using LgymApi.Application.TrainingPlanning.Contracts.PlanDay;
using LgymApi.Domain.ValueObjects;
using LgymApi.TrainingPlanning.Contracts;
using LgymApi.Application.WorkoutProgress.Persistence;

namespace LgymApi.Application.WorkoutProgress.Adapters;

internal sealed class PlanTrainingActivityAdapter(IWorkoutTrainingPersistence trainings) : IPlanTrainingActivityPort
{
    public async Task<IReadOnlyDictionary<Id<PlanDayReference>, DateTime?>> GetLastTrainingDatesAsync(
        IReadOnlyCollection<Id<PlanDayReference>> planDayIds,
        CancellationToken cancellationToken = default)
    {
        return await trainings.GetLastTrainingDatesAsync(planDayIds, cancellationToken);
    }
}
