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
using TrainerTraineeLinkEntity = LgymApi.Domain.Entities.TrainerTraineeLink;
using UserEntity = LgymApi.Domain.Entities.User;

namespace LgymApi.Application.Coaching.Invitations.Accept;

internal sealed class AcceptInvitationUseCase : IAcceptInvitationUseCase
{
    private readonly IAccountReadService _accounts;
    private readonly ICoachingInvitationPersistence _invitations;
    private readonly ICoachingActiveLinkPersistence _activeLinks;
    private readonly ICommandDispatcher _commands;
    private readonly IUnitOfWork _unitOfWork;
    private readonly IMapper _mapper;

    public AcceptInvitationUseCase(
        IAccountReadService accounts,
        ICoachingInvitationPersistence invitations,
        ICoachingActiveLinkPersistence activeLinks,
        ICommandDispatcher commands,
        IUnitOfWork unitOfWork,
        IMapper mapper)
    {
        _accounts = accounts;
        _invitations = invitations;
        _activeLinks = activeLinks;
        _commands = commands;
        _unitOfWork = unitOfWork;
        _mapper = mapper;
    }

    public async Task<Result<Unit, AppError>> ExecuteAsync(AcceptInvitationCommand command, CancellationToken cancellationToken = default)
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

        if (invitation.Status == TrainerInvitationStatus.Accepted
            && await _activeLinks.FindByTrainerAndTraineeAsync(invitation.TrainerId, command.TraineeId, cancellationToken) is not null)
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

        if (await _activeLinks.HasForTraineeAsync(command.TraineeId, cancellationToken))
        {
            return Result<Unit, AppError>.Failure(new InvalidTrainerRelationshipError(Messages.TraineeAlreadyLinked));
        }

        try
        {
            var link = _mapper.Map<InvitationActiveLinkSource, CoachingActiveLinkWriteModel>(
                new InvitationActiveLinkSource(Id<TrainerTraineeLinkEntity>.New(), invitation.TrainerId, command.TraineeId),
                _mapper.CreateContext());
            await _activeLinks.AddAsync(link, cancellationToken);
            await _invitations.UpdateResponseAsync(
                MapResponse(invitation.Id, traineeIdToBind, TrainerInvitationStatus.Accepted, now),
                cancellationToken);
            await _unitOfWork.SaveChangesAsync(cancellationToken);
        }
        catch
        {
            if (await _activeLinks.HasForTraineeAsync(command.TraineeId, cancellationToken))
            {
                return Result<Unit, AppError>.Failure(new InvalidTrainerRelationshipError(Messages.TraineeAlreadyLinked));
            }

            throw;
        }

        await _commands.EnqueueAsync(new InvitationAcceptedCommand { InvitationId = invitation.Id });
        await _commands.EnqueueAsync(new TrainerInvitationAcceptedInAppNotificationCommand
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
