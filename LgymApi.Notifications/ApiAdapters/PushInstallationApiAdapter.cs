using LgymApi.Application.BuildingBlocks.Errors;
using LgymApi.Application.BuildingBlocks.Results;
using LgymApi.Application.Notifications;
using LgymApi.Application.Notifications.Models;
using LgymApi.Domain.Entities;
using LgymApi.Domain.ValueObjects;
using LgymApi.Identity.Contracts;

namespace LgymApi.Notifications.ApiAdapters;

public interface IPushInstallationApiAdapter
{
    Task<Result<Unit, AppError>> RegisterAsync(
        Id<AccountReference>? accountId,
        Id<AccountSessionReference>? sessionId,
        RegisterPushInstallationInput input,
        CancellationToken cancellationToken = default);

    Task<Result<Unit, AppError>> UnregisterAsync(
        Id<AccountReference>? accountId,
        Id<AccountSessionReference>? sessionId,
        PushInstallationActionInput input,
        CancellationToken cancellationToken = default);

    Task<Result<Unit, AppError>> DisassociateAsync(
        Id<AccountReference>? accountId,
        Id<AccountSessionReference>? sessionId,
        PushInstallationActionInput input,
        CancellationToken cancellationToken = default);
}

internal sealed class PushInstallationApiAdapter(
    IPushInstallationLifecycleService pushInstallationLifecycleService) : IPushInstallationApiAdapter
{
    public Task<Result<Unit, AppError>> RegisterAsync(
        Id<AccountReference>? accountId,
        Id<AccountSessionReference>? sessionId,
        RegisterPushInstallationInput input,
        CancellationToken cancellationToken = default)
        => pushInstallationLifecycleService.RegisterAsync(accountId?.Rebind<User>(), sessionId?.Rebind<UserSession>(), input, cancellationToken);

    public Task<Result<Unit, AppError>> UnregisterAsync(
        Id<AccountReference>? accountId,
        Id<AccountSessionReference>? sessionId,
        PushInstallationActionInput input,
        CancellationToken cancellationToken = default)
        => pushInstallationLifecycleService.UnregisterAsync(accountId?.Rebind<User>(), sessionId?.Rebind<UserSession>(), input, cancellationToken);

    public Task<Result<Unit, AppError>> DisassociateAsync(
        Id<AccountReference>? accountId,
        Id<AccountSessionReference>? sessionId,
        PushInstallationActionInput input,
        CancellationToken cancellationToken = default)
        => pushInstallationLifecycleService.DisassociateAsync(accountId?.Rebind<User>(), sessionId?.Rebind<UserSession>(), input, cancellationToken);
}
