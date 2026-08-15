using System.Diagnostics;
using LgymApi.Application.Notifications.Repositories;
using LgymApi.Application.Repositories;
using Microsoft.Extensions.Logging;

namespace LgymApi.Application.Notifications;

internal sealed class DisabledPushInstallationRetentionCleanupService : IDisabledPushInstallationRetentionCleanupService
{
    private readonly IPushInstallationRepository _pushInstallationRepository;
    private readonly IUnitOfWork _unitOfWork;
    private readonly INotificationRetentionSettings _settings;
    private readonly ILogger<DisabledPushInstallationRetentionCleanupService> _logger;

    public DisabledPushInstallationRetentionCleanupService(
        IPushInstallationRepository pushInstallationRepository,
        IUnitOfWork unitOfWork,
        INotificationRetentionSettings settings,
        ILogger<DisabledPushInstallationRetentionCleanupService> logger)
    {
        _pushInstallationRepository = pushInstallationRepository;
        _unitOfWork = unitOfWork;
        _settings = settings;
        _logger = logger;
    }

    public async Task<int> CleanupAsync(CancellationToken cancellationToken = default)
    {
        const string operation = "push-disabled-installation-retention-cleanup";
        var cutoff = DateTimeOffset.UtcNow.AddDays(-_settings.DisabledInstallationDays);
        var stopwatch = Stopwatch.StartNew();
        var removedCount = 0;
        var batchCount = 0;

        _logger.LogInformation(
            "Retention cleanup {Operation} started before cutoff {CutoffUtc} with batch size {BatchSize}.",
            operation,
            cutoff,
            _settings.BatchSize);

        try
        {
            while (true)
            {
                var candidates = await _pushInstallationRepository.GetRetentionCandidatesDisabledBeforeAsync(
                    cutoff,
                    _settings.BatchSize,
                    cancellationToken);
                if (candidates.Count == 0)
                {
                    break;
                }

                _pushInstallationRepository.RemoveRange(candidates);
                await _unitOfWork.SaveChangesAsync(cancellationToken);
                removedCount += candidates.Count;
                batchCount++;
            }

            _logger.LogInformation(
                "Retention cleanup {Operation} completed with {DeletedCount} rows in {BatchCount} batches before cutoff {CutoffUtc} in {Duration}.",
                operation,
                removedCount,
                batchCount,
                cutoff,
                stopwatch.Elapsed);
            return removedCount;
        }
        catch (Exception exception)
        {
            _logger.LogError(
                exception,
                "Retention cleanup {Operation} failed after deleting {DeletedCount} rows in {BatchCount} batches before cutoff {CutoffUtc} in {Duration}.",
                operation,
                removedCount,
                batchCount,
                cutoff,
                stopwatch.Elapsed);
            throw;
        }
    }
}
