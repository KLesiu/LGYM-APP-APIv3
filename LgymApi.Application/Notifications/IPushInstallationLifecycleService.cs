using LgymApi.Application.BuildingBlocks.Errors;
using LgymApi.Application.BuildingBlocks.Results;
using LgymApi.Application.Notifications.Models;
using LgymApi.Domain.Entities;
using LgymApi.Domain.ValueObjects;

namespace LgymApi.Application.Notifications;

public interface IPushInstallationLifecycleService
{
    Task<Result<Unit, AppError>> RegisterAsync(
        Id<User>? currentUserId,
        Id<UserSession>? sessionId,
        RegisterPushInstallationInput input,
        CancellationToken cancellationToken = default);

    Task<Result<Unit, AppError>> UnregisterAsync(
        Id<User>? currentUserId,
        Id<UserSession>? sessionId,
        PushInstallationActionInput input,
        CancellationToken cancellationToken = default);

    Task<Result<Unit, AppError>> DisassociateAsync(
        Id<User>? currentUserId,
        Id<UserSession>? sessionId,
        PushInstallationActionInput input,
        CancellationToken cancellationToken = default);
}
