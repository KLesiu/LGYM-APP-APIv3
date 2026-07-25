using LgymApi.Application.BuildingBlocks.Errors;
using LgymApi.Application.BuildingBlocks.Results;

namespace LgymApi.Application.Coaching.Invitations.Revoke;

public interface IRevokeInvitationUseCase
{
    Task<Result<Unit, AppError>> ExecuteAsync(RevokeInvitationCommand command, CancellationToken cancellationToken = default);
}
