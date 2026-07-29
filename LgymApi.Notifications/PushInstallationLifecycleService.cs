using LgymApi.Application.BuildingBlocks.Errors;
using LgymApi.Application.Identity.Errors;
using LgymApi.Application.BuildingBlocks.Results;
using LgymApi.Application.Notifications.Models;
using LgymApi.Application.Notifications.Repositories;
using LgymApi.Application.Repositories;
using LgymApi.Domain.Entities;
using LgymApi.Domain.ValueObjects;
using LgymApi.Identity.Contracts;
using LgymApi.Resources;

namespace LgymApi.Application.Notifications;

internal sealed class PushInstallationLifecycleService : IPushInstallationLifecycleService
{
    private const string UnregisteredDisabledReason = "Unregistered";

    private readonly IPushInstallationRepository _pushInstallationRepository;
    private readonly IUnitOfWork _unitOfWork;

    public PushInstallationLifecycleService(
        IPushInstallationRepository pushInstallationRepository,
        IUnitOfWork unitOfWork)
    {
        _pushInstallationRepository = pushInstallationRepository;
        _unitOfWork = unitOfWork;
    }

    public async Task<Result<Unit, AppError>> RegisterAsync(
        Id<User>? currentUserId,
        Id<UserSession>? sessionId,
        RegisterPushInstallationInput input,
        CancellationToken cancellationToken = default)
    {
        if (!currentUserId.HasValue || !sessionId.HasValue)
        {
            return Result<Unit, AppError>.Failure(new UserUnauthorizedError(Messages.Unauthorized));
        }

        var normalizedInstallationId = NormalizeRequiredValue(input.InstallationKey);
        var normalizedPlatform = NormalizeRequiredValue(input.Platform);
        var normalizedToken = NormalizeRequiredValue(input.FcmToken);
        var normalizedEnvironment = NormalizeRequiredValue(input.Environment);

        if (normalizedInstallationId == null
            || normalizedPlatform == null
            || normalizedToken == null
            || normalizedEnvironment == null)
        {
            return Result<Unit, AppError>.Failure(new InvalidUserError(Messages.FieldRequired));
        }

        await _pushInstallationRepository.UpsertForUserSessionAsync(
            new PushInstallationRegistration(
                normalizedInstallationId,
                normalizedPlatform,
                normalizedToken,
                NormalizeOptionalValue(input.AppVersion),
                normalizedEnvironment,
                NormalizeOptionalValue(input.PermissionStatus),
                currentUserId.Value,
                sessionId.Value,
                DateTimeOffset.UtcNow),
            cancellationToken);

        await _unitOfWork.SaveChangesAsync(cancellationToken);
        return Result<Unit, AppError>.Success(Unit.Value);
    }

    public async Task<Result<Unit, AppError>> UnregisterAsync(
        Id<User>? currentUserId,
        Id<UserSession>? sessionId,
        PushInstallationActionInput input,
        CancellationToken cancellationToken = default)
    {
        if (!currentUserId.HasValue || !sessionId.HasValue)
        {
            return Result<Unit, AppError>.Failure(new UserUnauthorizedError(Messages.Unauthorized));
        }

        var normalizedInstallationId = NormalizeRequiredValue(input.InstallationKey);
        if (normalizedInstallationId == null)
        {
            return Result<Unit, AppError>.Failure(new InvalidUserError(Messages.FieldRequired));
        }

        await _pushInstallationRepository.DisableBoundForUserOrSessionAsync(
            normalizedInstallationId,
            currentUserId.Value,
            sessionId.Value,
            DateTimeOffset.UtcNow,
            UnregisteredDisabledReason,
            cancellationToken);
        await _unitOfWork.SaveChangesAsync(cancellationToken);
        return Result<Unit, AppError>.Success(Unit.Value);
    }

    public async Task<Result<Unit, AppError>> DisassociateAsync(
        Id<User>? currentUserId,
        Id<UserSession>? sessionId,
        PushInstallationActionInput input,
        CancellationToken cancellationToken = default)
    {
        if (!currentUserId.HasValue || !sessionId.HasValue)
        {
            return Result<Unit, AppError>.Failure(new UserUnauthorizedError(Messages.Unauthorized));
        }

        var normalizedInstallationId = NormalizeRequiredValue(input.InstallationKey);
        if (normalizedInstallationId == null)
        {
            return Result<Unit, AppError>.Failure(new InvalidUserError(Messages.FieldRequired));
        }

        await _pushInstallationRepository.DisassociateBoundForUserOrSessionAsync(
            normalizedInstallationId,
            currentUserId.Value,
            sessionId.Value,
            DateTimeOffset.UtcNow,
            cancellationToken);
        await _unitOfWork.SaveChangesAsync(cancellationToken);
        return Result<Unit, AppError>.Success(Unit.Value);
    }

    public Task StageDisassociateForSessionAsync(
        Id<AccountSessionReference> sessionId,
        CancellationToken cancellationToken = default)
    {
        return _pushInstallationRepository.DisassociateForSessionAsync(sessionId, DateTimeOffset.UtcNow, cancellationToken);
    }

    private static string? NormalizeRequiredValue(string? value)
    {
        var normalized = NormalizeOptionalValue(value);
        return string.IsNullOrWhiteSpace(normalized) ? null : normalized;
    }

    private static string? NormalizeOptionalValue(string? value)
    {
        return string.IsNullOrWhiteSpace(value) ? null : value.Trim();
    }
}
