using LgymApi.Application.BuildingBlocks.Errors;
using LgymApi.Application.BuildingBlocks.Results;

namespace LgymApi.Application.Coaching.Invitations.Accept;

public interface IAcceptInvitationUseCase
{
    Task<Result<Unit, AppError>> ExecuteAsync(AcceptInvitationCommand command, CancellationToken cancellationToken = default);
}
