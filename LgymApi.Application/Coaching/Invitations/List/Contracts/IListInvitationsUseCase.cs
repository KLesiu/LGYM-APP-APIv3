using LgymApi.Application.BuildingBlocks.Errors;
using LgymApi.Application.BuildingBlocks.Results;
using LgymApi.Application.Coaching.Invitations.Models;

namespace LgymApi.Application.Coaching.Invitations.List;

public interface IListInvitationsUseCase
{
    Task<Result<IReadOnlyList<InvitationReadModel>, AppError>> ExecuteAsync(ListInvitationsQuery query, CancellationToken cancellationToken = default);
}
