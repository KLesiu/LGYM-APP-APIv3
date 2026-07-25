using LgymApi.Application.BuildingBlocks.Errors;
using LgymApi.Application.BuildingBlocks.Results;
using LgymApi.Application.Coaching.Invitations.Models;

namespace LgymApi.Application.Coaching.Invitations.CreateByEmail;

public interface ICreateInvitationByEmailUseCase
{
    Task<Result<InvitationReadModel, AppError>> ExecuteAsync(CreateInvitationByEmailCommand command, CancellationToken cancellationToken = default);
}
