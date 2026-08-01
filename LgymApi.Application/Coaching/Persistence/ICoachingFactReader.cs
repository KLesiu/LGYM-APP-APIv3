using LgymApi.Domain.Entities;
using LgymApi.Domain.ValueObjects;

namespace LgymApi.Application.Coaching.Persistence;

public interface ICoachingFactReader
{
    Task<IReadOnlyList<CoachingInvitationFact>> GetInvitationFactsAsync(Id<User> trainerId, CancellationToken cancellationToken = default);
    Task<IReadOnlyList<CoachingDashboardFact>> GetDashboardFactsAsync(Id<User> trainerId, CancellationToken cancellationToken = default);
}
