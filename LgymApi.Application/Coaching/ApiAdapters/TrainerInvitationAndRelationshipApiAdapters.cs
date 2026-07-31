using LgymApi.Application.BuildingBlocks.Errors;
using LgymApi.Application.BuildingBlocks.Results;
using LgymApi.Application.Coaching.Invitations.Accept;
using LgymApi.Application.Coaching.Invitations.Create;
using LgymApi.Application.Coaching.Invitations.CreateByEmail;
using LgymApi.Application.Coaching.Invitations.ListPaginated;
using LgymApi.Application.Coaching.Invitations.Models;
using LgymApi.Application.Coaching.Invitations.Reject;
using LgymApi.Application.Coaching.Invitations.Revoke;
using LgymApi.Application.Coaching.ManagedPlans.GetActive;
using LgymApi.Application.Coaching.Relationships.DetachFromTrainer;
using LgymApi.Application.Coaching.Relationships.GetCurrentTrainer;
using LgymApi.Application.Mapping.Core;
using LgymApi.Application.Pagination;
using LgymApi.Application.TrainingPlanning.Contracts.ManagedPlans;
using LgymApi.Domain.Entities;
using LgymApi.Domain.ValueObjects;
using LgymApi.Identity.Contracts;
using LgymApi.Identity.Contracts.Accounts;

namespace LgymApi.Application.Coaching.ApiAdapters;

internal sealed class TrainerInvitationApiAdapter : ITrainerInvitationApiPort
{
    private readonly ICreateInvitationUseCase _createInvitation;
    private readonly ICreateInvitationByEmailUseCase _createInvitationByEmail;
    private readonly IListPaginatedInvitationsUseCase _listInvitations;
    private readonly IRevokeInvitationUseCase _revokeInvitation;
    private readonly IMapper _mapper;

    public TrainerInvitationApiAdapter(ICreateInvitationUseCase createInvitation, ICreateInvitationByEmailUseCase createInvitationByEmail, IListPaginatedInvitationsUseCase listInvitations, IRevokeInvitationUseCase revokeInvitation, IMapper mapper)
    {
        _createInvitation = createInvitation;
        _createInvitationByEmail = createInvitationByEmail;
        _listInvitations = listInvitations;
        _revokeInvitation = revokeInvitation;
        _mapper = mapper;
    }

    public Task<Result<InvitationReadModel, AppError>> CreateAsync(AuthenticatedAccountContext trainer, Id<AccountReference> traineeId, CancellationToken cancellationToken = default)
        => _createInvitation.ExecuteAsync(_mapper.Map<TrainerTraineeAccountInput, CreateInvitationCommand>(new(trainer.Id, traineeId)), cancellationToken);

    public Task<Result<InvitationReadModel, AppError>> CreateByEmailAsync(AuthenticatedAccountContext trainer, string email, string preferredLanguage, string preferredTimeZone, CancellationToken cancellationToken = default)
        => _createInvitationByEmail.ExecuteAsync(_mapper.Map<ActorEmailAccountInput, CreateInvitationByEmailCommand>(new(trainer.Id, email, preferredLanguage, preferredTimeZone)), cancellationToken);

    public Task<Result<Pagination<InvitationReadModel>, AppError>> GetPaginatedAsync(AuthenticatedAccountContext trainer, FilterInput filter, CancellationToken cancellationToken = default)
        => _listInvitations.ExecuteAsync(_mapper.Map<ActorFilterAccountInput, ListPaginatedInvitationsQuery>(new(trainer.Id, filter)), cancellationToken);

    public Task<Result<Unit, AppError>> RevokeAsync(AuthenticatedAccountContext trainer, Id<TrainerInvitation> invitationId, CancellationToken cancellationToken = default)
        => _revokeInvitation.ExecuteAsync(_mapper.Map<ActorInvitationAccountInput, RevokeInvitationCommand>(new(trainer.Id, invitationId)), cancellationToken);
}

internal sealed class TraineeRelationshipApiAdapter : ITraineeRelationshipApiPort
{
    private readonly IAcceptInvitationUseCase _acceptInvitation;
    private readonly IRejectInvitationUseCase _rejectInvitation;
    private readonly IDetachFromTrainerUseCase _detachFromTrainer;
    private readonly IGetCurrentTrainerUseCase _getCurrentTrainer;
    private readonly IGetActiveManagedPlanUseCase _getActivePlan;
    private readonly IMapper _mapper;

    public TraineeRelationshipApiAdapter(IAcceptInvitationUseCase acceptInvitation, IRejectInvitationUseCase rejectInvitation, IDetachFromTrainerUseCase detachFromTrainer, IGetCurrentTrainerUseCase getCurrentTrainer, IGetActiveManagedPlanUseCase getActivePlan, IMapper mapper)
    {
        _acceptInvitation = acceptInvitation;
        _rejectInvitation = rejectInvitation;
        _detachFromTrainer = detachFromTrainer;
        _getCurrentTrainer = getCurrentTrainer;
        _getActivePlan = getActivePlan;
        _mapper = mapper;
    }

    public Task<Result<Unit, AppError>> AcceptInvitationAsync(AuthenticatedAccountContext trainee, Id<TrainerInvitation> invitationId, CancellationToken cancellationToken = default)
        => _acceptInvitation.ExecuteAsync(_mapper.Map<ActorInvitationAccountInput, AcceptInvitationCommand>(new(trainee.Id, invitationId)), cancellationToken);

    public Task<Result<Unit, AppError>> RejectInvitationAsync(AuthenticatedAccountContext trainee, Id<TrainerInvitation> invitationId, CancellationToken cancellationToken = default)
        => _rejectInvitation.ExecuteAsync(_mapper.Map<ActorInvitationAccountInput, RejectInvitationCommand>(new(trainee.Id, invitationId)), cancellationToken);

    public Task<Result<Unit, AppError>> DetachAsync(AuthenticatedAccountContext trainee, CancellationToken cancellationToken = default)
        => _detachFromTrainer.ExecuteAsync(_mapper.Map<ActorAccountInput, DetachFromTrainerCommand>(new(trainee.Id)), cancellationToken);

    public Task<Result<CurrentTrainerReadModel, AppError>> GetCurrentTrainerAsync(AuthenticatedAccountContext trainee, CancellationToken cancellationToken = default)
        => _getCurrentTrainer.ExecuteAsync(_mapper.Map<ActorAccountInput, GetCurrentTrainerQuery>(new(trainee.Id)), cancellationToken);

    public Task<Result<ManagedPlanReadModel, AppError>> GetActivePlanAsync(AuthenticatedAccountContext trainee, CancellationToken cancellationToken = default)
        => _getActivePlan.ExecuteAsync(_mapper.Map<ActorAccountInput, GetActiveManagedPlanQuery>(new(trainee.Id)), cancellationToken);
}
