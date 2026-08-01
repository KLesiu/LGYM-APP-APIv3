using LgymApi.Application.Coaching.Persistence;
using LgymApi.Application.Mapping.Core;
using LgymApi.Domain.Entities;
using LgymApi.Domain.ValueObjects;
using LgymApi.Infrastructure.Data;
using LgymApi.Identity.Contracts;
using Microsoft.EntityFrameworkCore;

namespace LgymApi.Infrastructure.Repositories.Coaching;

public sealed class CoachingActiveLinkPersistenceRepository : ICoachingActiveLinkPersistence
{
    private readonly AppDbContext _dbContext;
    private readonly IMapper _mapper;

    public CoachingActiveLinkPersistenceRepository(AppDbContext dbContext, IMapper mapper)
    {
        _dbContext = dbContext;
        _mapper = mapper;
    }

    public Task AddAsync(CoachingActiveLinkWriteModel link, CancellationToken cancellationToken = default)
    {
        var entity = _mapper.Map<CoachingActiveLinkWriteModel, TrainerTraineeLink>(link, _mapper.CreateContext());
        return _dbContext.TrainerTraineeLinks.AddAsync(entity, cancellationToken).AsTask();
    }

    public async Task RemoveAsync(Id<TrainerTraineeLink> linkId, CancellationToken cancellationToken = default)
    {
        var link = await _dbContext.TrainerTraineeLinks.FirstOrDefaultAsync(candidate => candidate.Id == linkId, cancellationToken);
        if (link is not null)
        {
            _dbContext.TrainerTraineeLinks.Remove(link);
        }
    }

    public Task<bool> HasForTraineeAsync(Id<User> traineeId, CancellationToken cancellationToken = default)
        => _dbContext.TrainerTraineeLinks.AnyAsync(link => link.TraineeId == traineeId, cancellationToken);

    public Task<CoachingActiveLinkFact?> FindByTrainerAndTraineeAsync(Id<User> trainerId, Id<User> traineeId, CancellationToken cancellationToken = default)
        => FindAsync(
            _dbContext.TrainerTraineeLinks
                .AsNoTracking()
                .Where(link => link.TrainerId == trainerId && link.TraineeId == traineeId),
            cancellationToken);

    public Task<CoachingActiveLinkFact?> FindByTraineeAsync(Id<User> traineeId, CancellationToken cancellationToken = default)
        => FindAsync(_dbContext.TrainerTraineeLinks.AsNoTracking().Where(link => link.TraineeId == traineeId), cancellationToken);

    public Task<bool> HasActiveRelationshipAsync(Id<AccountReference> trainerId, Id<AccountReference> traineeId, CancellationToken cancellationToken = default)
    {
        var persistedTrainerId = trainerId.Rebind<User>();
        var persistedTraineeId = traineeId.Rebind<User>();
        return _dbContext.TrainerTraineeLinks.AsNoTracking().AnyAsync(
            link => link.TrainerId == persistedTrainerId && link.TraineeId == persistedTraineeId,
            cancellationToken);
    }

    private async Task<CoachingActiveLinkFact?> FindAsync(IQueryable<TrainerTraineeLink> query, CancellationToken cancellationToken)
    {
        var link = await query.FirstOrDefaultAsync(cancellationToken);
        return link is null
            ? null
            : _mapper.Map<TrainerTraineeLink, CoachingActiveLinkFact>(link, _mapper.CreateContext());
    }
}
