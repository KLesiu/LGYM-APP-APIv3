using LgymApi.Application.Coaching.Invitations.Models;
using LgymApi.Application.Coaching.Persistence;
using LgymApi.Application.BuildingBlocks.Errors;
using LgymApi.Application.Coaching.Errors;
using LgymApi.Application.BuildingBlocks.Results;
using LgymApi.Application.Identity.Contracts.Access;
using LgymApi.Application.Identity.Contracts.Accounts;
using LgymApi.Application.Mapping.Core;
using LgymApi.Application.Pagination;
using LgymApi.Resources;

namespace LgymApi.Application.Coaching.Invitations.ListPaginated;

internal sealed class ListPaginatedInvitationsUseCase : IListPaginatedInvitationsUseCase
{
    private readonly IUserAccessReadService _userAccess;
    private readonly ICoachingFactReader _facts;
    private readonly IAccountReadService _accounts;
    private readonly IQueryPaginationService _pagination;
    private readonly IMapper _mapper;

    public ListPaginatedInvitationsUseCase(IUserAccessReadService userAccess, ICoachingFactReader facts, IAccountReadService accounts, IQueryPaginationService pagination, IMapper mapper)
    {
        _userAccess = userAccess;
        _facts = facts;
        _accounts = accounts;
        _pagination = pagination;
        _mapper = mapper;
    }

    public async Task<Result<Pagination<InvitationReadModel>, AppError>> ExecuteAsync(ListPaginatedInvitationsQuery query, CancellationToken cancellationToken = default)
    {
        if (!await _userAccess.IsTrainerAsync(query.TrainerId, cancellationToken))
        {
            return Result<Pagination<InvitationReadModel>, AppError>.Failure(new TrainerRelationshipForbiddenError(Messages.TrainerRoleRequired));
        }

        var invitations = await _facts.GetInvitationFactsAsync(query.TrainerId, cancellationToken);
        var traineeIds = invitations
            .Where(invitation => invitation.TraineeId.HasValue)
            .Select(invitation => invitation.TraineeId!.Value)
            .ToList();
        var accounts = await _accounts.GetByIdsAsync(traineeIds, cancellationToken);
        var accountsById = accounts
            .GroupBy(account => account.Id)
            .ToDictionary(group => group.Key, group => group.First());
        var sources = invitations
            .Where(invitation => !invitation.TraineeId.HasValue || accountsById.ContainsKey(invitation.TraineeId.Value))
            .Select(invitation => new InvitationWithAccountSource(
                invitation,
                invitation.TraineeId.HasValue ? accountsById[invitation.TraineeId.Value] : null))
            .ToList();
        var enriched = _mapper.MapList<InvitationWithAccountSource, InvitationReadModel>(sources, _mapper.CreateContext());

        return await _pagination.ExecuteAsync(() => enriched.AsQueryable(), query.Filter, cancellationToken);
    }
}
