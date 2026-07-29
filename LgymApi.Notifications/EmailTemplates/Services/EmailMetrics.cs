using System.Diagnostics.Metrics;
using LgymApi.BackgroundWorker.Common.Notifications;
using LgymApi.Domain.Notifications;

namespace LgymApi.Infrastructure.Services;

internal sealed class EmailMetrics : IEmailMetrics
{
    private static readonly Meter Meter = new("LgymApi.Email", "1.0.0");
    private static readonly Counter<long> EnqueuedCounter = Meter.CreateCounter<long>("email_enqueued_total");
    private static readonly Counter<long> SentCounter = Meter.CreateCounter<long>("email_sent_total");
    private static readonly Counter<long> FailedCounter = Meter.CreateCounter<long>("email_failed_total");
    private static readonly Counter<long> RetriedCounter = Meter.CreateCounter<long>("email_retried_total");

    public void RecordEnqueued(EmailNotificationType notificationType)
    {
        EnqueuedCounter.Add(1, new KeyValuePair<string, object?>("notification_type", notificationType.Value));
    }

    public void RecordSent(EmailNotificationType notificationType)
    {
        SentCounter.Add(1, new KeyValuePair<string, object?>("notification_type", notificationType.Value));
    }

    public void RecordFailed(EmailNotificationType notificationType)
    {
        FailedCounter.Add(1, new KeyValuePair<string, object?>("notification_type", notificationType.Value));
    }

    public void RecordRetried(EmailNotificationType notificationType)
    {
        RetriedCounter.Add(1, new KeyValuePair<string, object?>("notification_type", notificationType.Value));
    }
}
