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

namespace LgymApi.Application.Coaching.Invitations.CreateByEmail;

internal sealed class CreateInvitationByEmailUseCase : ICreateInvitationByEmailUseCase
{
    private readonly IUserAccessReadService _userAccess;
    private readonly IAccountReadService _accounts;
    private readonly ICoachingInvitationPersistence _invitations;
    private readonly ICoachingActiveLinkPersistence _activeLinks;
    private readonly ICommandDispatcher _commands;
    private readonly IUnitOfWork _unitOfWork;
    private readonly IMapper _mapper;

    public CreateInvitationByEmailUseCase(IUserAccessReadService userAccess, IAccountReadService accounts, ICoachingInvitationPersistence invitations, ICoachingActiveLinkPersistence activeLinks, ICommandDispatcher commands, IUnitOfWork unitOfWork, IMapper mapper)
    {
        _userAccess = userAccess;
        _accounts = accounts;
        _invitations = invitations;
        _activeLinks = activeLinks;
        _commands = commands;
        _unitOfWork = unitOfWork;
        _mapper = mapper;
    }

    public async Task<Result<InvitationReadModel, AppError>> ExecuteAsync(CreateInvitationByEmailCommand command, CancellationToken cancellationToken = default)
    {
        if (!await _userAccess.IsTrainerAsync(command.TrainerId, cancellationToken))
        {
            return Result<InvitationReadModel, AppError>.Failure(new TrainerRelationshipForbiddenError(Messages.TrainerRoleRequired));
        }

        var trainer = await _accounts.GetByIdAsync(command.TrainerId, cancellationToken);
        if (trainer is null)
        {
            return Result<InvitationReadModel, AppError>.Failure(new TrainerRelationshipNotFoundError(Messages.DidntFind));
        }

        var inviteeEmail = new Email(command.InviteeEmail).Value;
        if (string.Equals(trainer.Email, inviteeEmail, StringComparison.OrdinalIgnoreCase))
        {
            return Result<InvitationReadModel, AppError>.Failure(new InvalidTrainerRelationshipError(Messages.CannotInviteYourself));
        }

        if (await _invitations.FindPendingByEmailAsync(command.TrainerId, inviteeEmail, cancellationToken) is not null)
        {
            return Result<InvitationReadModel, AppError>.Failure(new TrainerRelationshipConflictError(Messages.InvitationPendingForEmail));
        }

        var trainee = await _accounts.GetByEmailAsync(inviteeEmail, cancellationToken);
        if (trainee is not null
            && await _activeLinks.FindByTrainerAndTraineeAsync(command.TrainerId, trainee.Id, cancellationToken) is not null)
        {
            return Result<InvitationReadModel, AppError>.Failure(new TrainerRelationshipConflictError(Messages.EmailAlreadyYourTrainee));
        }

        if (trainee is not null && await _activeLinks.HasForTraineeAsync(trainee.Id, cancellationToken))
        {
            return Result<InvitationReadModel, AppError>.Failure(new TrainerRelationshipConflictError(Messages.TraineeAlreadyLinked));
        }

        var now = DateTimeOffset.UtcNow;
        var invitation = _mapper.Map<InvitationCreationSource, CoachingInvitationWriteModel>(
            new InvitationCreationSource(
            Id<TrainerInvitationEntity>.New(),
            command.TrainerId,
            inviteeEmail,
            trainee?.Id,
            CreateInvitationCode(),
            now.AddDays(7),
            now),
            _mapper.CreateContext());
        await _invitations.AddAsync(invitation, cancellationToken);
        await _commands.EnqueueAsync(new InvitationCreatedCommand { InvitationId = invitation.Id });
        if (trainee is not null)
        {
            await _commands.EnqueueAsync(new TrainerInvitationCreatedInAppNotificationCommand
            {
                InvitationId = invitation.Id,
                TraineeId = trainee.Id,
                TrainerId = command.TrainerId
            });
        }

        await _unitOfWork.SaveChangesAsync(cancellationToken);
        return Result<InvitationReadModel, AppError>.Success(_mapper.Map<CoachingInvitationWriteModel, InvitationReadModel>(invitation, _mapper.CreateContext()));
    }

    private static string CreateInvitationCode()
        => Id<TrainerInvitationEntity>.New().ToString().Replace("-", string.Empty, StringComparison.Ordinal)[..12].ToUpperInvariant();
}
