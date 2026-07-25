using LgymApi.Application.BuildingBlocks.Errors;
using LgymApi.Application.BuildingBlocks.Results;

namespace LgymApi.Application.Coaching.Invitations.PublicStatus;

public interface IPublicInvitationStatusUseCase
{
    Task<Result<PublicInvitationStatusReadModel, AppError>> ExecuteAsync(PublicInvitationStatusQuery query, CancellationToken cancellationToken = default);
}
