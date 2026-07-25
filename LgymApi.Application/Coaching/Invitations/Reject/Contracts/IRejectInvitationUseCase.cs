using LgymApi.Application.BuildingBlocks.Errors;
using LgymApi.Application.BuildingBlocks.Results;

namespace LgymApi.Application.Coaching.Invitations.Reject;

public interface IRejectInvitationUseCase
{
    Task<Result<Unit, AppError>> ExecuteAsync(RejectInvitationCommand command, CancellationToken cancellationToken = default);
}
