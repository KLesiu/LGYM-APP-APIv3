using LgymApi.Application.Coaching.Invitations.Models;
using LgymApi.Application.Coaching.Persistence;
using LgymApi.Application.BuildingBlocks.Errors;
using LgymApi.Application.Coaching.Errors;
using LgymApi.Application.BuildingBlocks.Results;
using LgymApi.Application.Identity.Contracts.Access;
using LgymApi.Application.Mapping.Core;
using LgymApi.Application.Repositories;
using LgymApi.Domain.Enums;
using LgymApi.Resources;

namespace LgymApi.Application.Coaching.Invitations.List;

internal sealed class ListInvitationsUseCase : IListInvitationsUseCase
{
    private readonly IUserAccessReadService _userAccess;
    private readonly ICoachingFactReader _facts;
    private readonly ICoachingInvitationPersistence _invitations;
    private readonly IUnitOfWork _unitOfWork;
    private readonly IMapper _mapper;

    public ListInvitationsUseCase(IUserAccessReadService userAccess, ICoachingFactReader facts, ICoachingInvitationPersistence invitations, IUnitOfWork unitOfWork, IMapper mapper)
    {
        _userAccess = userAccess;
        _facts = facts;
        _invitations = invitations;
        _unitOfWork = unitOfWork;
        _mapper = mapper;
    }

    public async Task<Result<IReadOnlyList<InvitationReadModel>, AppError>> ExecuteAsync(ListInvitationsQuery query, CancellationToken cancellationToken = default)
    {
        if (!await _userAccess.IsTrainerAsync(query.TrainerId, cancellationToken))
        {
            return Result<IReadOnlyList<InvitationReadModel>, AppError>.Failure(new TrainerRelationshipForbiddenError(Messages.TrainerRoleRequired));
        }

        var facts = await _facts.GetInvitationFactsAsync(query.TrainerId, cancellationToken);
        var now = DateTimeOffset.UtcNow;
        var sources = facts
            .OrderByDescending(invitation => invitation.CreatedAt)
            .Select(invitation => new InvitationListSource(
                invitation,
                invitation.Status == TrainerInvitationStatus.Pending && invitation.ExpiresAt <= now
                    ? TrainerInvitationStatus.Expired
                    : invitation.Status,
                invitation.Status == TrainerInvitationStatus.Pending && invitation.ExpiresAt <= now
                    ? now
                    : invitation.RespondedAt))
            .ToList();

        foreach (var source in sources.Where(source => source.Status == TrainerInvitationStatus.Expired && source.Invitation.Status == TrainerInvitationStatus.Pending))
        {
            await _invitations.ExpireAsync(source.Invitation.Id, source.RespondedAt!.Value, cancellationToken);
        }

        if (sources.Any(source => source.Status == TrainerInvitationStatus.Expired && source.Invitation.Status == TrainerInvitationStatus.Pending))
        {
            await _unitOfWork.SaveChangesAsync(cancellationToken);
        }

        return Result<IReadOnlyList<InvitationReadModel>, AppError>.Success(
            _mapper.MapList<InvitationListSource, InvitationReadModel>(sources, _mapper.CreateContext()));
    }
}
