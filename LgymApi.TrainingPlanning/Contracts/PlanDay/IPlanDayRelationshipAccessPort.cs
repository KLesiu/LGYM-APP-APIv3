using LgymApi.Domain.ValueObjects;
using LgymApi.Identity.Contracts;

namespace LgymApi.Application.TrainingPlanning.Contracts.PlanDay;

public interface IPlanDayRelationshipAccessPort
{
    Task<bool> HasActiveRelationshipAsync(
        Id<AccountReference> trainerId,
        Id<AccountReference> traineeId,
        CancellationToken cancellationToken = default);
}
