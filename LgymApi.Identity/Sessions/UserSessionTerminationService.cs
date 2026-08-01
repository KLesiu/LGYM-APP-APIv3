using LgymApi.Application.BuildingBlocks.Errors;
using LgymApi.Application.Identity.Errors;
using LgymApi.Application.BuildingBlocks.Results;
using LgymApi.Application.Identity.Contracts.Sessions;
using LgymApi.Application.Repositories;
using LgymApi.Application.Services;
using LgymApi.Domain.ValueObjects;
using LgymApi.Identity.Contracts;
using LgymApi.Resources;
using UserEntity = LgymApi.Domain.Entities.User;
using UserSessionEntity = LgymApi.Domain.Entities.UserSession;

namespace LgymApi.Application.Identity.Sessions;

internal sealed class UserSessionTerminationService : IUserSessionTerminationService
{
    private readonly IUserSessionStore _userSessionStore;
    private readonly IAccountSessionDisassociationPort _accountSessionDisassociationPort;
    private readonly IUnitOfWork _unitOfWork;

    public UserSessionTerminationService(
        IUserSessionStore userSessionStore,
        IAccountSessionDisassociationPort accountSessionDisassociationPort,
        IUnitOfWork unitOfWork)
    {
        _userSessionStore = userSessionStore;
        _accountSessionDisassociationPort = accountSessionDisassociationPort;
        _unitOfWork = unitOfWork;
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

        await _userSessionStore.RevokeSessionAsync(sessionId.Value, cancellationToken);
        await _accountSessionDisassociationPort.StageDisassociateAsync(
            currentUser.Id.Rebind<AccountReference>(),
            sessionId.Value.Rebind<AccountSessionReference>(),
            cancellationToken);
        await _unitOfWork.SaveChangesAsync(cancellationToken);

        return Result<Unit, AppError>.Success(Unit.Value);
    }
}
