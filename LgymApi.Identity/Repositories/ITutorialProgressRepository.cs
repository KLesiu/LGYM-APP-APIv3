using LgymApi.Domain.Entities;
using LgymApi.Domain.Enums;
using LgymApi.Domain.ValueObjects;

namespace LgymApi.Application.Repositories;

internal interface ITutorialProgressRepository
{
    Task<UserTutorialProgress?> FindByUserIdAndTypeAsync(Id<User> userId, TutorialType tutorialType, CancellationToken cancellationToken = default);
    Task<List<UserTutorialProgress>> GetActiveByUserIdAsync(Id<User> userId, CancellationToken cancellationToken = default);
    Task<bool> HasActiveTutorialsAsync(Id<User> userId, CancellationToken cancellationToken = default);
    Task AddAsync(UserTutorialProgress progress, CancellationToken cancellationToken = default);
    Task AddStepAsync(Id<UserTutorialProgress> progressId, UserTutorialStepProgress step, CancellationToken cancellationToken = default);
    Task UpdateAsync(UserTutorialProgress progress, CancellationToken cancellationToken = default);
}
