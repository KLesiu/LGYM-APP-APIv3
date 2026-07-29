using System.Text.Json;
using LgymApi.Application.Platform.Contracts.Serialization;
using LgymApi.Application.WorkoutProgress.Contracts.ReportingIntegration;
using LgymApi.Domain.Enums;
using LgymApi.Domain.ValueObjects;
using LgymApi.Identity.Contracts;

namespace LgymApi.Application.WorkoutProgress.Contracts.BackgroundActions;

public interface IReportSubmissionAcceptedProgressActionExecutionPort
{
    Task ExecuteAsync(string payloadJson, CancellationToken cancellationToken = default);
}

internal sealed class ReportSubmissionAcceptedProgressActionExecutionPort : IReportSubmissionAcceptedProgressActionExecutionPort
{
    private readonly IReportSubmissionAcceptedProgressConsumer _consumer;

    public ReportSubmissionAcceptedProgressActionExecutionPort(IReportSubmissionAcceptedProgressConsumer consumer)
    {
        _consumer = consumer ?? throw new ArgumentNullException(nameof(consumer));
    }

    public async Task ExecuteAsync(string payloadJson, CancellationToken cancellationToken = default)
    {
        AcceptedProgressWireEnvelope? envelope;
        try
        {
            envelope = JsonSerializer.Deserialize<AcceptedProgressWireEnvelope>(payloadJson, SharedSerializationOptions.Current);
        }
        catch (JsonException)
        {
            throw CreateSanitizedDeliveryFailure("Poison");
        }
        if (envelope?.Event is null)
        {
            throw CreateSanitizedDeliveryFailure("Poison");
        }

        if (!envelope.Event.TryToWorkoutEvent(out var workoutEvent, out var outcome))
        {
            throw CreateSanitizedDeliveryFailure(outcome);
        }

        var result = await _consumer.ConsumeAsync(workoutEvent, cancellationToken);
        if (result.Outcome is ReportSubmissionAcceptedProgressConsumeOutcome.Applied
            or ReportSubmissionAcceptedProgressConsumeOutcome.Duplicate)
        {
            return;
        }

        throw CreateSanitizedDeliveryFailure(result.Outcome.ToString());
    }

    private static InvalidOperationException CreateSanitizedDeliveryFailure(string outcome) =>
        new($"Report submission accepted-progress command delivery failed with outcome {outcome}.");
}

internal sealed class AcceptedProgressWireEnvelope
{
    public AcceptedProgressWirePayload? Event { get; init; }
}

internal sealed class AcceptedProgressWirePayload
{
    public int SchemaVersion { get; init; }
    public string EventId { get; init; } = string.Empty;
    public string ReportSubmissionId { get; init; } = string.Empty;
    public string CorrelationId { get; init; } = string.Empty;
    public string CausationId { get; init; } = string.Empty;
    public string TraineeId { get; init; } = string.Empty;
    public DateTimeOffset ObservedAt { get; init; }
    public DateTimeOffset AcceptedAt { get; init; }
    public IReadOnlyList<AcceptedProgressWireMeasurement?>? Measurements { get; init; }

    public bool TryToWorkoutEvent(out ReportSubmissionAcceptedProgressEvent workoutEvent, out string outcome)
    {
        workoutEvent = default!;
        outcome = "Invalid";
        if (SchemaVersion != 1)
        {
            outcome = "UnsupportedSchema";
            return false;
        }

        if (!IsCanonicalId(EventId) || !IsCanonicalId(ReportSubmissionId) || !IsCanonicalId(CorrelationId)
            || !IsCanonicalId(CausationId) || !Id<AccountReference>.TryParse(TraineeId, out var traineeId) || traineeId.IsEmpty || ObservedAt == default || AcceptedAt == default
            || Measurements is null || Measurements.Count == 0 || Measurements.Any(measurement => measurement is null || !measurement.IsValid()))
        {
            return false;
        }

        workoutEvent = new ReportSubmissionAcceptedProgressEvent(
            SchemaVersion, EventId, ReportSubmissionId, CorrelationId, CausationId, traineeId,
            ObservedAt, AcceptedAt,
            Measurements.Select(measurement => new ReportSubmissionAcceptedMeasurement(measurement!.BodyPart, measurement.Value, measurement.Unit)).ToArray());
        outcome = "Valid";
        return true;
    }

    private static bool IsCanonicalId(string value) =>
        !string.IsNullOrWhiteSpace(value)
        && Id<AcceptedProgressWirePayload>.TryParse(value, out var id)
        && string.Equals(value, id.ToString(), StringComparison.Ordinal);
}

internal sealed class AcceptedProgressWireMeasurement
{
    public BodyParts BodyPart { get; init; }
    public double Value { get; init; }
    public MeasurementUnits Unit { get; init; }

    public bool IsValid() => BodyPart != BodyParts.Unknown && Enum.IsDefined(BodyPart)
        && Unit != MeasurementUnits.Unknown && Enum.IsDefined(Unit) && double.IsFinite(Value) && Value > 0;
}
