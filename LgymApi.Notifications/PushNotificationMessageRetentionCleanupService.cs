using System.Diagnostics;
using LgymApi.Application.Repositories;
using Microsoft.Extensions.Logging;

namespace LgymApi.Application.Notifications;

internal sealed class PushNotificationMessageRetentionCleanupService : IPushNotificationMessageRetentionCleanupService
{
    private readonly IPushNotificationMessageRepository _pushNotificationMessageRepository;
    private readonly IUnitOfWork _unitOfWork;
    private readonly INotificationRetentionSettings _settings;
    private readonly ILogger<PushNotificationMessageRetentionCleanupService> _logger;

    public PushNotificationMessageRetentionCleanupService(
        IPushNotificationMessageRepository pushNotificationMessageRepository,
        IUnitOfWork unitOfWork,
        INotificationRetentionSettings settings,
        ILogger<PushNotificationMessageRetentionCleanupService> logger)
    {
        _pushNotificationMessageRepository = pushNotificationMessageRepository;
        _unitOfWork = unitOfWork;
        _settings = settings;
        _logger = logger;
    }

    public async Task<int> CleanupAsync(CancellationToken cancellationToken = default)
    {
        const string operation = "push-notification-message-retention-cleanup";
        var cutoff = DateTimeOffset.UtcNow.AddDays(-_settings.MessageHistoryDays);
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
                var candidates = await _pushNotificationMessageRepository.GetRetentionCandidatesCreatedBeforeAsync(
                    cutoff,
                    _settings.BatchSize,
                    cancellationToken);
                if (candidates.Count == 0)
                {
                    break;
                }

                _pushNotificationMessageRepository.RemoveRange(candidates);
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
