using LgymApi.Application.BuildingBlocks.Errors;
using LgymApi.Application.BuildingBlocks.Results;
using LgymApi.Application.Coaching.Invitations.Models;

namespace LgymApi.Application.Coaching.Invitations.Create;

public interface ICreateInvitationUseCase
{
    Task<Result<InvitationReadModel, AppError>> ExecuteAsync(CreateInvitationCommand command, CancellationToken cancellationToken = default);
}
