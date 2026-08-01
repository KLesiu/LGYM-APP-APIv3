using LgymApi.Application.Coaching.Persistence;
using LgymApi.Application.WorkoutProgress.Contracts.Measurements;
using LgymApi.Domain.ValueObjects;
using LgymApi.Domain.Security;
using LgymApi.Identity.Contracts;
using LgymApi.Identity.Contracts.Accounts;

namespace LgymApi.Application.Coaching.Adapters;

internal sealed class MeasurementsRelationshipAccessAdapter(
    IAccountAccessReader accountAccess,
    ICoachingActiveLinkPersistence activeLinks) : IMeasurementsRelationshipAccessPort
{
    public async Task<bool> HasActiveRelationshipAsync(
        Id<AccountReference> trainerId,
        Id<AccountReference> traineeId,
        CancellationToken cancellationToken = default)
    {
        var trainer = await accountAccess.GetByIdAsync(trainerId, cancellationToken);
        return trainer?.Roles.Contains(AuthConstants.Roles.Trainer, StringComparer.Ordinal) == true
            && await activeLinks.HasActiveRelationshipAsync(trainerId, traineeId, cancellationToken);
    }
}
