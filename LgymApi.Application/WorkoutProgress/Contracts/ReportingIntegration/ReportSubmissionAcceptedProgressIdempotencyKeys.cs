using System.Globalization;

namespace LgymApi.Application.WorkoutProgress.Contracts.ReportingIntegration;

public static class ReportSubmissionAcceptedProgressIdempotencyKeys
{
    private const string Prefix = "report-submission-accepted-progress";

    public static string CreateEventKey(ReportSubmissionAcceptedProgressEvent @event)
    {
        EnsureValid(@event);
        return $"{Prefix}:{@event.SchemaVersion}:event:{ReportSubmissionAcceptedProgressEvent.CanonicalizeId(@event.EventId)}";
    }

    public static string CreateMeasurementKey(
        ReportSubmissionAcceptedProgressEvent @event,
        ReportSubmissionAcceptedMeasurement measurement)
    {
        EnsureValid(@event);
        if (!ReportSubmissionAcceptedProgressEvent.IsValidMeasurement(measurement))
        {
            throw new ArgumentException("Measurement must be valid.", nameof(measurement));
        }

        var observedAt = @event.ObservedAt
            .ToUniversalTime()
            .ToString("O", CultureInfo.InvariantCulture);
        return $"{Prefix}:{@event.SchemaVersion}:measurement:{ReportSubmissionAcceptedProgressEvent.CanonicalizeId(@event.ReportSubmissionId)}:{@event.TraineeId}:{measurement.BodyPart}:{observedAt}";
    }

    private static void EnsureValid(ReportSubmissionAcceptedProgressEvent @event)
    {
        ArgumentNullException.ThrowIfNull(@event);

        var validation = @event.Validate();
        if (!validation.IsValid)
        {
            throw new ArgumentException(validation.Reason, nameof(@event));
        }
    }
}
