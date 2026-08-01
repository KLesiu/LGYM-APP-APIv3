using LgymApi.Domain.ValueObjects;
using LgymApi.Identity.Contracts;

namespace LgymApi.Application.WorkoutProgress.Contracts.Measurements;

public interface IMeasurementsRelationshipAccessPort
{
    Task<bool> HasActiveRelationshipAsync(
        Id<AccountReference> trainerId,
        Id<AccountReference> traineeId,
        CancellationToken cancellationToken = default);
}
