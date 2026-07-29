using LgymApi.Application.WorkoutProgress.Persistence;
using LgymApi.Domain.Entities;
using LgymApi.Domain.ValueObjects;
using LgymApi.Identity.Contracts;
using LgymApi.Infrastructure.Data;
using Microsoft.EntityFrameworkCore;

namespace LgymApi.Infrastructure.Repositories.WorkoutProgress;

public sealed class WorkoutGymPersistenceRepository(AppDbContext dbContext) : IWorkoutGymPersistence
{
    public Task AddAsync(WorkoutGymWriteModel gym, CancellationToken cancellationToken = default)
        => dbContext.Gyms.AddAsync(ToEntity(gym), cancellationToken).AsTask();

    public async Task<WorkoutGymPersistenceModel?> FindByIdAsync(Id<Gym> id, CancellationToken cancellationToken = default)
    {
        var entity = await dbContext.Gyms.AsNoTracking().FirstOrDefaultAsync(gym => gym.Id == id, cancellationToken);
        return entity is null ? null : WorkoutPersistenceProjection.Gym(entity);
    }

    public async Task<IReadOnlyList<WorkoutGymPersistenceModel>> GetByAccountIdAsync(Id<AccountReference> accountId, CancellationToken cancellationToken = default)
        => (await dbContext.Gyms.AsNoTracking().Where(gym => gym.UserId == WorkoutPersistenceAccountIds.ToPersisted(accountId) && !gym.IsDeleted).ToListAsync(cancellationToken)).Select(WorkoutPersistenceProjection.Gym).ToList();

    public async Task UpdateAsync(WorkoutGymWriteModel gym, CancellationToken cancellationToken = default)
    {
        var entity = await dbContext.Gyms.FirstAsync(item => item.Id == gym.Id, cancellationToken);
        entity.UserId = WorkoutPersistenceAccountIds.ToPersisted(gym.OwnerId);
        entity.Name = gym.Name;
        entity.AddressId = gym.AddressId;
        entity.IsDeleted = gym.IsDeleted;
    }

    private static Gym ToEntity(WorkoutGymWriteModel model) => new() { Id = model.Id, UserId = WorkoutPersistenceAccountIds.ToPersisted(model.OwnerId), Name = model.Name, AddressId = model.AddressId, IsDeleted = model.IsDeleted };
}
