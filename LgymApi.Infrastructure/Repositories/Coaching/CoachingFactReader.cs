using LgymApi.Application.Coaching.Persistence;
using LgymApi.Application.Mapping.Core;
using LgymApi.Domain.Entities;
using LgymApi.Domain.ValueObjects;
using LgymApi.Infrastructure.Data;
using Microsoft.EntityFrameworkCore;

namespace LgymApi.Infrastructure.Repositories.Coaching;

public sealed class CoachingFactReader : ICoachingFactReader
{
    private readonly AppDbContext _dbContext;
    private readonly IMapper _mapper;

    public CoachingFactReader(AppDbContext dbContext, IMapper mapper)
    {
        _dbContext = dbContext;
        _mapper = mapper;
    }

    public async Task<IReadOnlyList<CoachingInvitationFact>> GetInvitationFactsAsync(Id<User> trainerId, CancellationToken cancellationToken = default)
    {
        var invitations = await _dbContext.TrainerInvitations
            .AsNoTracking()
            .Where(invitation => invitation.TrainerId == trainerId)
            .ToListAsync(cancellationToken);

        return _mapper.MapList<TrainerInvitation, CoachingInvitationFact>(invitations, _mapper.CreateContext());
    }

    public async Task<IReadOnlyList<CoachingDashboardFact>> GetDashboardFactsAsync(Id<User> trainerId, CancellationToken cancellationToken = default)
    {
        var links = await _dbContext.TrainerTraineeLinks
            .AsNoTracking()
            .Where(link => link.TrainerId == trainerId)
            .ToListAsync(cancellationToken);
        var invitations = await _dbContext.TrainerInvitations
            .AsNoTracking()
            .Where(invitation => invitation.TrainerId == trainerId)
            .ToListAsync(cancellationToken);
        var linkFacts = _mapper.MapList<TrainerTraineeLink, CoachingActiveLinkFact>(links, _mapper.CreateContext());
        var invitationFacts = _mapper.MapList<TrainerInvitation, CoachingInvitationFact>(invitations, _mapper.CreateContext());
        var linksByTrainee = linkFacts.ToDictionary(link => link.TraineeId);
        var latestInvitationsByTrainee = invitationFacts
            .Where(invitation => invitation.TraineeId.HasValue)
            .GroupBy(invitation => invitation.TraineeId!.Value)
            .ToDictionary(
                group => group.Key,
                group => group
                    .OrderByDescending(invitation => invitation.CreatedAt)
                    .ThenByDescending(invitation => invitation.Id)
                    .First());
        var traineeIds = linksByTrainee.Keys.Concat(latestInvitationsByTrainee.Keys).Distinct();

        var dashboardSources = traineeIds
            .Select(traineeId => new CoachingDashboardSource(
                traineeId,
                linksByTrainee.GetValueOrDefault(traineeId),
                latestInvitationsByTrainee.GetValueOrDefault(traineeId)))
            .ToList();

        return _mapper.MapList<CoachingDashboardSource, CoachingDashboardFact>(dashboardSources, _mapper.CreateContext());
    }
}
