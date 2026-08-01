using LgymApi.Application.Coaching.Contracts.BackgroundCommands;
using LgymApi.Application.Coaching.Persistence;
using LgymApi.Application.BuildingBlocks.Errors;
using LgymApi.Application.Coaching.Errors;
using LgymApi.Application.BuildingBlocks.Results;
using LgymApi.Application.Identity.Contracts.Access;
using LgymApi.Application.Mapping.Core;
using LgymApi.Application.Platform.Contracts.BackgroundCommands;
using LgymApi.Application.Repositories;
using LgymApi.Domain.Enums;
using LgymApi.Domain.ValueObjects;
using LgymApi.Resources;
using TrainerInvitationEntity = LgymApi.Domain.Entities.TrainerInvitation;
using UserEntity = LgymApi.Domain.Entities.User;

namespace LgymApi.Application.Coaching.Invitations.Revoke;

internal sealed class RevokeInvitationUseCase : IRevokeInvitationUseCase
{
    private readonly IUserAccessReadService _userAccess;
    private readonly ICoachingInvitationPersistence _invitations;
    private readonly ICommandDispatcher _commands;
    private readonly IUnitOfWork _unitOfWork;
    private readonly IMapper _mapper;

    public RevokeInvitationUseCase(
        IUserAccessReadService userAccess,
        ICoachingInvitationPersistence invitations,
        ICommandDispatcher commands,
        IUnitOfWork unitOfWork,
        IMapper mapper)
    {
        _userAccess = userAccess;
        _invitations = invitations;
        _commands = commands;
        _unitOfWork = unitOfWork;
        _mapper = mapper;
    }

    public async Task<Result<Unit, AppError>> ExecuteAsync(RevokeInvitationCommand command, CancellationToken cancellationToken = default)
    {
        if (!await _userAccess.IsTrainerAsync(command.TrainerId, cancellationToken))
        {
            return Result<Unit, AppError>.Failure(new TrainerRelationshipForbiddenError(Messages.TrainerRoleRequired));
        }

        if (command.InvitationId.IsEmpty)
        {
            return Result<Unit, AppError>.Failure(new InvalidTrainerRelationshipError(Messages.FieldRequired));
        }

        var invitation = await _invitations.FindByIdAsync(command.InvitationId, cancellationToken);
        if (invitation is null || invitation.TrainerId != command.TrainerId)
        {
            return Result<Unit, AppError>.Failure(new TrainerRelationshipNotFoundError(Messages.DidntFind));
        }

        if (invitation.Status != TrainerInvitationStatus.Pending)
        {
            return Result<Unit, AppError>.Failure(new InvalidTrainerRelationshipError(Messages.InvitationNoLongerPending));
        }

        var update = _mapper.Map<InvitationResponseSource, CoachingInvitationResponseUpdateModel>(
            new InvitationResponseSource(invitation.Id, null, TrainerInvitationStatus.Revoked, DateTimeOffset.UtcNow),
            _mapper.CreateContext());
        await _invitations.UpdateResponseAsync(update, cancellationToken);
        await _unitOfWork.SaveChangesAsync(cancellationToken);
        await _commands.EnqueueAsync(new InvitationRevokedCommand { InvitationId = invitation.Id });

        return Result<Unit, AppError>.Success(Unit.Value);
    }
}
