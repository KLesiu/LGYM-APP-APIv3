using LgymApi.Application.BuildingBlocks.Errors;
using LgymApi.Application.Identity.Errors;
using LgymApi.Application.BuildingBlocks.Results;
using LgymApi.Application.Identity.Contracts.Sessions;
using LgymApi.Domain.ValueObjects;
using LgymApi.Identity.Contracts;
using LgymApi.Resources;
using UserEntity = LgymApi.Domain.Entities.User;
using UserSessionEntity = LgymApi.Domain.Entities.UserSession;

namespace LgymApi.Application.Identity.Sessions;

internal sealed class UserSessionTerminationService : IUserSessionTerminationService
{
    private readonly UserSessionTerminationServiceDependencies _dependencies;

    public UserSessionTerminationService(UserSessionTerminationServiceDependencies dependencies)
    {
        _dependencies = dependencies;
    }

    public async Task<Result<Unit, AppError>> LogoutAsync(
        UserEntity? currentUser,
        Id<UserSessionEntity>? sessionId,
        CancellationToken cancellationToken = default)
    {
        if (currentUser == null)
        {
            return Result<Unit, AppError>.Failure(new UserNotFoundError(Messages.DidntFind));
        }

        if (!sessionId.HasValue)
        {
            return Result<Unit, AppError>.Success(Unit.Value);
        }

        await _dependencies.UserSessionStore.RevokeSessionAsync(sessionId.Value, cancellationToken);
        await _dependencies.AccountSessionDisassociationPort.StageDisassociateAsync(
            currentUser.Id.Rebind<AccountReference>(),
            sessionId.Value.Rebind<AccountSessionReference>(),
            cancellationToken);
        await _dependencies.UnitOfWork.SaveChangesAsync(cancellationToken);

        return Result<Unit, AppError>.Success(Unit.Value);
    }
}
