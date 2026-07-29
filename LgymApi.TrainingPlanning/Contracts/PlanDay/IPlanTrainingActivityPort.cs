using LgymApi.Domain.ValueObjects;
using LgymApi.TrainingPlanning.Contracts;

namespace LgymApi.Application.TrainingPlanning.Contracts.PlanDay;

public interface IPlanTrainingActivityPort
{
    Task<IReadOnlyDictionary<Id<PlanDayReference>, DateTime?>> GetLastTrainingDatesAsync(
        IReadOnlyCollection<Id<PlanDayReference>> planDayIds,
        CancellationToken cancellationToken = default);
}
