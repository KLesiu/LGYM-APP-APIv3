using LgymApi.Application.Coaching.Persistence;
using LgymApi.Application.Mapping.Core;
using LgymApi.Domain.Entities;
using LgymApi.Domain.Enums;
using LgymApi.Domain.ValueObjects;
using LgymApi.Infrastructure.Data;
using Microsoft.EntityFrameworkCore;

namespace LgymApi.Infrastructure.Repositories.Coaching;

public sealed class CoachingInvitationPersistenceRepository : ICoachingInvitationPersistence
{
    private readonly AppDbContext _dbContext;
    private readonly IMapper _mapper;

    public CoachingInvitationPersistenceRepository(AppDbContext dbContext, IMapper mapper)
    {
        _dbContext = dbContext;
        _mapper = mapper;
    }

    public Task AddAsync(CoachingInvitationWriteModel invitation, CancellationToken cancellationToken = default)
    {
        var entity = _mapper.Map<CoachingInvitationWriteModel, TrainerInvitation>(invitation, _mapper.CreateContext());
        return _dbContext.TrainerInvitations.AddAsync(entity, cancellationToken).AsTask();
    }

    public Task<CoachingInvitationFact?> FindByIdAsync(Id<TrainerInvitation> invitationId, CancellationToken cancellationToken = default)
        => FindAsync(_dbContext.TrainerInvitations.AsNoTracking().Where(invitation => invitation.Id == invitationId), cancellationToken);

    public Task<CoachingInvitationFact?> FindPendingAsync(Id<User> trainerId, Id<User> traineeId, CancellationToken cancellationToken = default)
        => FindAsync(
            _dbContext.TrainerInvitations
                .AsNoTracking()
                .Where(invitation => invitation.TrainerId == trainerId
                    && invitation.TraineeId == traineeId
                    && invitation.Status == TrainerInvitationStatus.Pending)
                .OrderByDescending(invitation => invitation.CreatedAt),
            cancellationToken);

    public Task<CoachingInvitationFact?> FindPendingByEmailAsync(Id<User> trainerId, string inviteeEmail, CancellationToken cancellationToken = default)
        => FindAsync(
            _dbContext.TrainerInvitations
                .AsNoTracking()
                .Where(invitation => invitation.TrainerId == trainerId
                    && invitation.InviteeEmail == inviteeEmail
                    && invitation.Status == TrainerInvitationStatus.Pending)
                .OrderByDescending(invitation => invitation.CreatedAt),
            cancellationToken);

    public Task<CoachingInvitationFact?> FindByIdAndCodeAsync(Id<TrainerInvitation> invitationId, string code, CancellationToken cancellationToken = default)
        => FindAsync(
            _dbContext.TrainerInvitations
                .AsNoTracking()
                .Where(invitation => invitation.Id == invitationId && invitation.Code == code),
            cancellationToken);

    public Task ExpireAsync(Id<TrainerInvitation> invitationId, DateTimeOffset respondedAt, CancellationToken cancellationToken = default)
        => UpdateResponseAsync(
            new CoachingInvitationResponseUpdateModel(
                invitationId,
                null,
                TrainerInvitationStatus.Expired,
                respondedAt),
            cancellationToken);

    public Task UpdateResponseAsync(CoachingInvitationResponseUpdateModel update, CancellationToken cancellationToken = default)
    {
        var invitation = _mapper.Map<CoachingInvitationResponseUpdateModel, TrainerInvitation>(update, _mapper.CreateContext());
        _dbContext.TrainerInvitations.Attach(invitation);
        _dbContext.Entry(invitation).Property(candidate => candidate.Status).IsModified = true;
        _dbContext.Entry(invitation).Property(candidate => candidate.RespondedAt).IsModified = true;
        if (update.TraineeId.HasValue)
        {
            _dbContext.Entry(invitation).Property(candidate => candidate.TraineeId).IsModified = true;
        }

        return Task.CompletedTask;
    }

    private async Task<CoachingInvitationFact?> FindAsync(IQueryable<TrainerInvitation> query, CancellationToken cancellationToken)
    {
        var invitation = await query.FirstOrDefaultAsync(cancellationToken);
        return invitation is null
            ? null
            : _mapper.Map<TrainerInvitation, CoachingInvitationFact>(invitation, _mapper.CreateContext());
    }
}
