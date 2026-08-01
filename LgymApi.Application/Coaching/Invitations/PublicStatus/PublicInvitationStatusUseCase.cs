using LgymApi.Application.Coaching.Persistence;
using LgymApi.Application.BuildingBlocks.Errors;
using LgymApi.Application.Coaching.Errors;
using LgymApi.Application.BuildingBlocks.Results;
using LgymApi.Application.Identity.Contracts.Accounts;
using LgymApi.Application.Mapping.Core;
using LgymApi.Resources;

namespace LgymApi.Application.Coaching.Invitations.PublicStatus;

internal sealed class PublicInvitationStatusUseCase : IPublicInvitationStatusUseCase
{
    private readonly ICoachingInvitationPersistence _invitations;
    private readonly IAccountReadService _accounts;
    private readonly IMapper _mapper;

    public PublicInvitationStatusUseCase(ICoachingInvitationPersistence invitations, IAccountReadService accounts, IMapper mapper)
    {
        _invitations = invitations;
        _accounts = accounts;
        _mapper = mapper;
    }

    public async Task<Result<PublicInvitationStatusReadModel, AppError>> ExecuteAsync(PublicInvitationStatusQuery query, CancellationToken cancellationToken = default)
    {
        if (query.InvitationId.IsEmpty || string.IsNullOrWhiteSpace(query.Code))
        {
            return Result<PublicInvitationStatusReadModel, AppError>.Failure(new TrainerRelationshipNotFoundError(Messages.DidntFind));
        }

        var invitation = await _invitations.FindByIdAndCodeAsync(query.InvitationId, query.Code, cancellationToken);
        if (invitation is null)
        {
            return Result<PublicInvitationStatusReadModel, AppError>.Failure(new TrainerRelationshipNotFoundError(Messages.DidntFind));
        }

        var userExists = invitation.TraineeId.HasValue || (!string.IsNullOrWhiteSpace(invitation.InviteeEmail)
            && await _accounts.GetByEmailAsync(invitation.InviteeEmail, cancellationToken) is not null);
        return Result<PublicInvitationStatusReadModel, AppError>.Success(
            _mapper.Map<PublicInvitationStatusSource, PublicInvitationStatusReadModel>(
                new PublicInvitationStatusSource(invitation, userExists),
                _mapper.CreateContext()));
    }
}
