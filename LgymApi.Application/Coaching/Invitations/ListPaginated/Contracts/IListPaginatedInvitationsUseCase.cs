using LgymApi.Application.BuildingBlocks.Errors;
using LgymApi.Application.BuildingBlocks.Results;
using LgymApi.Application.Coaching.Invitations.Models;
using LgymApi.Application.Pagination;

namespace LgymApi.Application.Coaching.Invitations.ListPaginated;

public interface IListPaginatedInvitationsUseCase
{
    Task<Result<Pagination<InvitationReadModel>, AppError>> ExecuteAsync(ListPaginatedInvitationsQuery query, CancellationToken cancellationToken = default);
}
