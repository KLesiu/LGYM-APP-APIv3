using LgymApi.Application.Repositories;
using LgymApi.Application.Options;
using LgymApi.BackgroundWorker.Common;
using LgymApi.Domain.Enums;
using LgymApi.Infrastructure.Data;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

namespace LgymApi.Infrastructure.Services;

public sealed class CommittedIntentDispatcher : ICommittedIntentDispatcher
{
    private const int BatchSize = 100;

    private readonly IServiceScopeFactory _scopeFactory;
    private readonly ILogger<CommittedIntentDispatcher> _logger;
    private readonly BackgroundCommandOptions _backgroundCommandOptions;

    public CommittedIntentDispatcher(
        IServiceScopeFactory scopeFactory,
        ILogger<CommittedIntentDispatcher> logger,
        BackgroundCommandOptions backgroundCommandOptions)
    {
        _scopeFactory = scopeFactory;
        _logger = logger;
        _backgroundCommandOptions = backgroundCommandOptions;
    }

    public async Task DispatchCommittedIntentsAsync(CancellationToken cancellationToken = default)
    {
        using var scope = _scopeFactory.CreateScope();
        var dbContext = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var actionScheduler = scope.ServiceProvider.GetRequiredService<IActionMessageScheduler>();
        var emailScheduler = scope.ServiceProvider.GetRequiredService<IEmailBackgroundScheduler>();

        await RecoverStaleProcessingEnvelopesAsync(dbContext, cancellationToken);
        await DispatchCommandEnvelopesAsync(dbContext, actionScheduler, cancellationToken);
        await RecoverStaleSendingNotificationsAsync(dbContext, emailScheduler, cancellationToken);
        await DispatchNotificationMessagesAsync(dbContext, emailScheduler, cancellationToken);
    }

    private async Task RecoverStaleProcessingEnvelopesAsync(AppDbContext dbContext, CancellationToken cancellationToken)
    {
        _logger.LogDebug(
            "Automatic recovery of processing command envelopes is disabled to avoid double-dispatch without a heartbeat/lease-renewal mechanism. Configured timeout remains {ProcessingLeaseTimeoutMinutes} minute(s).",
            _backgroundCommandOptions.ProcessingLeaseTimeoutMinutes);

        await Task.CompletedTask;
    }

    private async Task DispatchCommandEnvelopesAsync(
        AppDbContext dbContext,
        IActionMessageScheduler scheduler,
        CancellationToken cancellationToken)
    {
        var pendingEnvelopes = await dbContext.CommandEnvelopes
            .Where(x => x.Status == ActionExecutionStatus.Pending && x.DispatchedAt == null)
            .OrderBy(x => x.CreatedAt)
            .Take(BatchSize)
            .ToListAsync(cancellationToken);

        foreach (var envelope in pendingEnvelopes)
        {
            try
            {
                var schedulerJobId = scheduler.Enqueue(envelope.Id.ToString());
                envelope.DispatchedAt = DateTimeOffset.UtcNow;
                envelope.SchedulerJobId = schedulerJobId;
                await dbContext.SaveChangesAsync(cancellationToken);
            }
            catch (Exception ex)
            {
                _logger.LogWarning(
                    ex,
                    "Failed to dispatch committed command envelope {EnvelopeId}. It remains recoverable as undispatched.",
                    envelope.Id);
            }
        }
    }

    private async Task DispatchNotificationMessagesAsync(
        AppDbContext dbContext,
        IEmailBackgroundScheduler scheduler,
        CancellationToken cancellationToken)
    {
        var pendingNotifications = await dbContext.NotificationMessages
            .Where(x => x.Status == EmailNotificationStatus.Pending && x.DispatchedAt == null && !x.IsDeadLettered)
            .OrderBy(x => x.CreatedAt)
            .Take(BatchSize)
            .ToListAsync(cancellationToken);

        foreach (var notification in pendingNotifications)
        {
            try
            {
                var schedulerJobId = scheduler.Enqueue(notification.Id.ToString());
                notification.DispatchedAt = DateTimeOffset.UtcNow;
                notification.SchedulerJobId = schedulerJobId;
                await dbContext.SaveChangesAsync(cancellationToken);
            }
            catch (Exception ex)
            {
                _logger.LogWarning(
                    ex,
                    "Failed to dispatch committed notification message {NotificationId}. It remains recoverable as undispatched.",
                    notification.Id);
            }
        }
    }

    private async Task RecoverStaleSendingNotificationsAsync(
        AppDbContext dbContext,
        IEmailBackgroundScheduler scheduler,
        CancellationToken cancellationToken)
    {
        var leaseCutoff = DateTimeOffset.UtcNow.AddSeconds(-_backgroundCommandOptions.EmailSendLeaseSeconds);

        var staleSendingNotifications = await dbContext.NotificationMessages
            .Where(x => x.Status == EmailNotificationStatus.Sending
                        && x.DeliveredAt == null
                        && !x.IsDeadLettered
                        && (x.LastAttemptAt == null || x.LastAttemptAt < leaseCutoff))
            .OrderBy(x => x.LastAttemptAt)
            .ThenBy(x => x.CreatedAt)
            .Take(BatchSize)
            .ToListAsync(cancellationToken);

        foreach (var notification in staleSendingNotifications)
        {
            try
            {
                var schedulerJobId = scheduler.Enqueue(notification.Id.ToString());
                notification.DispatchedAt = DateTimeOffset.UtcNow;
                notification.SchedulerJobId = schedulerJobId;
                await dbContext.SaveChangesAsync(cancellationToken);
            }
            catch (Exception ex)
            {
                _logger.LogWarning(
                    ex,
                    "Failed to redispatch stale sending notification message {NotificationId}. It remains recoverable for a later committed-intent dispatch pass.",
                    notification.Id);
            }
        }
    }
}
