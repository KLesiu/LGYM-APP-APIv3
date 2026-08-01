using LgymApi.Application.Coaching.Contracts.BackgroundCommands;
using LgymApi.Application.Coaching.Persistence;
using LgymApi.Application.BuildingBlocks.Errors;
using LgymApi.Application.Coaching.Errors;
using LgymApi.Application.BuildingBlocks.Results;
using LgymApi.Application.Identity.Contracts.Accounts;
using LgymApi.Application.Mapping.Core;
using LgymApi.Application.Platform.Contracts.BackgroundCommands;
using LgymApi.Application.Repositories;
using LgymApi.Domain.Enums;
using LgymApi.Domain.ValueObjects;
using LgymApi.Resources;
using TrainerInvitationEntity = LgymApi.Domain.Entities.TrainerInvitation;
using UserEntity = LgymApi.Domain.Entities.User;

namespace LgymApi.Application.Coaching.Invitations.Reject;

internal sealed class RejectInvitationUseCase : IRejectInvitationUseCase
{
    private readonly IAccountReadService _accounts;
    private readonly ICoachingInvitationPersistence _invitations;
    private readonly ICommandDispatcher _commands;
    private readonly IUnitOfWork _unitOfWork;
    private readonly IMapper _mapper;

    public RejectInvitationUseCase(
        IAccountReadService accounts,
        ICoachingInvitationPersistence invitations,
        ICommandDispatcher commands,
        IUnitOfWork unitOfWork,
        IMapper mapper)
    {
        _accounts = accounts;
        _invitations = invitations;
        _commands = commands;
        _unitOfWork = unitOfWork;
        _mapper = mapper;
    }

    public async Task<Result<Unit, AppError>> ExecuteAsync(RejectInvitationCommand command, CancellationToken cancellationToken = default)
    {
        if (command.InvitationId.IsEmpty)
        {
            return Result<Unit, AppError>.Failure(new InvalidTrainerRelationshipError(Messages.FieldRequired));
        }

        var invitation = await _invitations.FindByIdAsync(command.InvitationId, cancellationToken);
        if (invitation is null)
        {
            return Result<Unit, AppError>.Failure(new TrainerRelationshipNotFoundError(Messages.DidntFind));
        }

        Id<UserEntity>? traineeIdToBind = null;
        if (invitation.TraineeId.HasValue)
        {
            if (invitation.TraineeId.Value != command.TraineeId)
            {
                return Result<Unit, AppError>.Failure(new TrainerRelationshipNotFoundError(Messages.DidntFind));
            }
        }
        else
        {
            var trainee = await _accounts.GetByIdAsync(command.TraineeId, cancellationToken);
            if (trainee is null
                || new Email(invitation.InviteeEmail).Value != new Email(trainee.Email).Value)
            {
                return Result<Unit, AppError>.Failure(new TrainerRelationshipNotFoundError(Messages.DidntFind));
            }

            traineeIdToBind = command.TraineeId;
        }

        if (invitation.Status == TrainerInvitationStatus.Rejected)
        {
            return Result<Unit, AppError>.Success(Unit.Value);
        }

        var now = DateTimeOffset.UtcNow;
        if (invitation.ExpiresAt <= now)
        {
            if (invitation.Status == TrainerInvitationStatus.Pending)
            {
                await _invitations.UpdateResponseAsync(
                    MapResponse(invitation.Id, null, TrainerInvitationStatus.Expired, now),
                    cancellationToken);
                await _unitOfWork.SaveChangesAsync(cancellationToken);
            }

            return Result<Unit, AppError>.Failure(new InvalidTrainerRelationshipError(Messages.InvitationExpired));
        }

        if (invitation.Status != TrainerInvitationStatus.Pending)
        {
            return Result<Unit, AppError>.Failure(new InvalidTrainerRelationshipError(Messages.InvitationNoLongerPending));
        }

        await _invitations.UpdateResponseAsync(
            MapResponse(invitation.Id, traineeIdToBind, TrainerInvitationStatus.Rejected, now),
            cancellationToken);
        await _unitOfWork.SaveChangesAsync(cancellationToken);
        await _commands.EnqueueAsync(new TrainerInvitationRejectedInAppNotificationCommand
        {
            InvitationId = invitation.Id,
            TrainerId = invitation.TrainerId,
            TraineeId = command.TraineeId
        });

        return Result<Unit, AppError>.Success(Unit.Value);
    }

    private CoachingInvitationResponseUpdateModel MapResponse(
        Id<TrainerInvitationEntity> invitationId,
        Id<UserEntity>? traineeId,
        TrainerInvitationStatus status,
        DateTimeOffset respondedAt)
        => _mapper.Map<InvitationResponseSource, CoachingInvitationResponseUpdateModel>(
            new InvitationResponseSource(invitationId, traineeId, status, respondedAt),
            _mapper.CreateContext());
}
