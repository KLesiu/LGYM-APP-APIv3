using LgymApi.Application.Repositories;
using LgymApi.Domain.Entities;
using LgymApi.Domain.Enums;
using LgymApi.Domain.ValueObjects;
using LgymApi.Identity.Persistence;
using Microsoft.EntityFrameworkCore;

namespace LgymApi.Infrastructure.Repositories;

internal sealed class TutorialProgressRepository : ITutorialProgressRepository
{
    private readonly IIdentityPersistenceContext _dbContext;

    public TutorialProgressRepository(IIdentityPersistenceContext dbContext)
    {
        _dbContext = dbContext;
    }

    public Task<UserTutorialProgress?> FindByUserIdAndTypeAsync(Id<User> userId, TutorialType tutorialType, CancellationToken cancellationToken = default)
    {
        return _dbContext.UserTutorialProgresses
            .AsTracking()
            .Include(p => p.CompletedSteps)
            .FirstOrDefaultAsync(p => p.UserId == userId && p.TutorialType == tutorialType, cancellationToken);
    }

    public Task<List<UserTutorialProgress>> GetActiveByUserIdAsync(Id<User> userId, CancellationToken cancellationToken = default)
    {
        return _dbContext.UserTutorialProgresses
            .Include(p => p.CompletedSteps)
            .Where(p => p.UserId == userId && !p.IsCompleted)
            .ToListAsync(cancellationToken);
    }

    public async Task<bool> HasActiveTutorialsAsync(Id<User> userId, CancellationToken cancellationToken = default)
    {
        return await _dbContext.UserTutorialProgresses
            .AnyAsync(p => p.UserId == userId && !p.IsCompleted, cancellationToken);
    }

    public Task AddAsync(UserTutorialProgress progress, CancellationToken cancellationToken = default)
    {
        return _dbContext.UserTutorialProgresses.AddAsync(progress, cancellationToken).AsTask();
    }

    public Task UpdateAsync(UserTutorialProgress progress, CancellationToken cancellationToken = default) => Task.CompletedTask;

    public Task AddStepAsync(Id<UserTutorialProgress> progressId, UserTutorialStepProgress step, CancellationToken cancellationToken = default)
    {
        return _dbContext.UserTutorialStepProgresses.AddAsync(step, cancellationToken).AsTask();
    }
}
