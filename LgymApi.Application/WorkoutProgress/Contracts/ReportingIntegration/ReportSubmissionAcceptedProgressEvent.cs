using LgymApi.Domain.Enums;
using LgymApi.Domain.ValueObjects;
using LgymApi.Identity.Contracts;

namespace LgymApi.Application.WorkoutProgress.Contracts.ReportingIntegration;

public sealed record ReportSubmissionAcceptedProgressEvent(
    int SchemaVersion,
    string EventId,
    string ReportSubmissionId,
    string CorrelationId,
    string CausationId,
    Id<AccountReference> TraineeId,
    DateTimeOffset ObservedAt,
    DateTimeOffset AcceptedAt,
    IReadOnlyList<ReportSubmissionAcceptedMeasurement> Measurements)
{
    public const int CurrentSchemaVersion = 1;

    public ReportSubmissionAcceptedProgressValidationResult Validate()
    {
        if (SchemaVersion != CurrentSchemaVersion)
        {
            return ReportSubmissionAcceptedProgressValidationResult.UnsupportedSchema(
                $"Schema version '{SchemaVersion}' is not supported.");
        }

        if (!IsCanonicalId(EventId)
            || !IsCanonicalId(ReportSubmissionId)
            || !IsCanonicalId(CorrelationId)
            || !IsCanonicalId(CausationId)
            || TraineeId.IsEmpty
            || ObservedAt == default
            || AcceptedAt == default
            || Measurements == null
            || Measurements.Count == 0
            || Measurements.Any(measurement => !IsValidMeasurement(measurement)))
        {
            return ReportSubmissionAcceptedProgressValidationResult.Invalid(
                "The accepted report submission event contains invalid identifiers, timestamps, or measurements.");
        }

        return ReportSubmissionAcceptedProgressValidationResult.Valid();
    }

    internal static bool IsValidMeasurement(ReportSubmissionAcceptedMeasurement measurement)
    {
        return measurement != null
            && measurement.BodyPart != BodyParts.Unknown
            && Enum.IsDefined(measurement.BodyPart)
            && measurement.Unit != MeasurementUnits.Unknown
            && Enum.IsDefined(measurement.Unit)
            && double.IsFinite(measurement.Value)
            && measurement.Value > 0;
    }

    internal static string CanonicalizeId(string value)
    {
        Id<ReportSubmissionAcceptedProgressEvent>.TryParse(value, out var id);
        return id.ToString();
    }

    private static bool IsCanonicalId(string? value)
    {
        return !string.IsNullOrWhiteSpace(value)
            && Id<ReportSubmissionAcceptedProgressEvent>.TryParse(value, out var id)
            && string.Equals(value, id.ToString(), StringComparison.Ordinal);
    }
}

public sealed record ReportSubmissionAcceptedMeasurement(
    BodyParts BodyPart,
    double Value,
    MeasurementUnits Unit);

public enum ReportSubmissionAcceptedProgressValidationOutcome
{
    Valid = 0,
    Invalid = 1,
    UnsupportedSchema = 2
}

public sealed record ReportSubmissionAcceptedProgressValidationResult(
    ReportSubmissionAcceptedProgressValidationOutcome Outcome,
    string? Reason)
{
    public bool IsValid => Outcome == ReportSubmissionAcceptedProgressValidationOutcome.Valid;

    public static ReportSubmissionAcceptedProgressValidationResult Valid()
        => new(ReportSubmissionAcceptedProgressValidationOutcome.Valid, null);

    public static ReportSubmissionAcceptedProgressValidationResult Invalid(string reason)
        => new(ReportSubmissionAcceptedProgressValidationOutcome.Invalid, reason);

    public static ReportSubmissionAcceptedProgressValidationResult UnsupportedSchema(string reason)
        => new(ReportSubmissionAcceptedProgressValidationOutcome.UnsupportedSchema, reason);
}
