using LgymApi.Application.Coaching.Contracts.BackgroundCommands;
using LgymApi.Application.Coaching.Invitations.Models;
using LgymApi.Application.Coaching.Persistence;
using LgymApi.Application.BuildingBlocks.Errors;
using LgymApi.Application.Coaching.Errors;
using LgymApi.Application.BuildingBlocks.Results;
using LgymApi.Application.Identity.Contracts.Access;
using LgymApi.Application.Identity.Contracts.Accounts;
using LgymApi.Application.Mapping.Core;
using LgymApi.Application.Platform.Contracts.BackgroundCommands;
using LgymApi.Application.Repositories;
using LgymApi.Domain.ValueObjects;
using LgymApi.Resources;
using TrainerInvitationEntity = LgymApi.Domain.Entities.TrainerInvitation;

namespace LgymApi.Application.Coaching.Invitations.Create;

internal sealed class CreateInvitationUseCase : ICreateInvitationUseCase
{
    private readonly IUserAccessReadService _userAccess;
    private readonly IAccountReadService _accounts;
    private readonly ICoachingInvitationPersistence _invitations;
    private readonly ICoachingActiveLinkPersistence _activeLinks;
    private readonly ICommandDispatcher _commands;
    private readonly IUnitOfWork _unitOfWork;
    private readonly IMapper _mapper;

    public CreateInvitationUseCase(IUserAccessReadService userAccess, IAccountReadService accounts, ICoachingInvitationPersistence invitations, ICoachingActiveLinkPersistence activeLinks, ICommandDispatcher commands, IUnitOfWork unitOfWork, IMapper mapper)
    {
        _userAccess = userAccess;
        _accounts = accounts;
        _invitations = invitations;
        _activeLinks = activeLinks;
        _commands = commands;
        _unitOfWork = unitOfWork;
        _mapper = mapper;
    }

    public async Task<Result<InvitationReadModel, AppError>> ExecuteAsync(CreateInvitationCommand command, CancellationToken cancellationToken = default)
    {
        if (!await _userAccess.IsTrainerAsync(command.TrainerId, cancellationToken))
        {
            return Result<InvitationReadModel, AppError>.Failure(new TrainerRelationshipForbiddenError(Messages.TrainerRoleRequired));
        }

        if (command.TraineeId.IsEmpty)
        {
            return Result<InvitationReadModel, AppError>.Failure(new InvalidTrainerRelationshipError(Messages.UserIdRequired));
        }

        if (command.TrainerId == command.TraineeId)
        {
            return Result<InvitationReadModel, AppError>.Failure(new InvalidTrainerRelationshipError(Messages.CannotInviteYourself));
        }

        if (await _accounts.GetByIdAsync(command.TraineeId, cancellationToken) is null)
        {
            return Result<InvitationReadModel, AppError>.Failure(new TrainerRelationshipNotFoundError(Messages.DidntFind));
        }

        if (await _activeLinks.HasForTraineeAsync(command.TraineeId, cancellationToken))
        {
            return Result<InvitationReadModel, AppError>.Failure(new InvalidTrainerRelationshipError(Messages.TraineeAlreadyLinked));
        }

        var existing = await _invitations.FindPendingAsync(command.TrainerId, command.TraineeId, cancellationToken);
        if (existing is not null)
        {
            if (existing.ExpiresAt > DateTimeOffset.UtcNow)
            {
                return Result<InvitationReadModel, AppError>.Success(_mapper.Map<CoachingInvitationFact, InvitationReadModel>(existing, _mapper.CreateContext()));
            }

            await _invitations.ExpireAsync(existing.Id, DateTimeOffset.UtcNow, cancellationToken);
            await _unitOfWork.SaveChangesAsync(cancellationToken);
        }

        var now = DateTimeOffset.UtcNow;
        var invitation = _mapper.Map<InvitationCreationSource, CoachingInvitationWriteModel>(
            new InvitationCreationSource(
            Id<TrainerInvitationEntity>.New(),
            command.TrainerId,
            string.Empty,
            command.TraineeId,
            CreateInvitationCode(),
            now.AddDays(7),
            now),
            _mapper.CreateContext());
        await _invitations.AddAsync(invitation, cancellationToken);
        await _commands.EnqueueAsync(new InvitationCreatedCommand { InvitationId = invitation.Id });
        await _commands.EnqueueAsync(new TrainerInvitationCreatedInAppNotificationCommand
        {
            InvitationId = invitation.Id,
            TraineeId = command.TraineeId,
            TrainerId = command.TrainerId
        });
        await _unitOfWork.SaveChangesAsync(cancellationToken);

        return Result<InvitationReadModel, AppError>.Success(_mapper.Map<CoachingInvitationWriteModel, InvitationReadModel>(invitation, _mapper.CreateContext()));
    }

    private static string CreateInvitationCode()
        => Id<TrainerInvitationEntity>.New().ToString().Replace("-", string.Empty, StringComparison.Ordinal)[..12].ToUpperInvariant();
}
