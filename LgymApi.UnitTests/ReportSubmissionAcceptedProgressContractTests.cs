using System.Reflection;
using System.Text.Json;
using FluentAssertions;
using LgymApi.Application.Platform.Contracts.Serialization;
using LgymApi.Application.Reporting.Contracts.BackgroundCommands;
using LgymApi.Application.WorkoutProgress.Contracts.ReportingIntegration;
using LgymApi.Domain.Entities;
using LgymApi.Domain.Enums;
using LgymApi.Domain.ValueObjects;
using LgymApi.Identity.Contracts;
using WorkoutAcceptedProgressEvent = LgymApi.Application.WorkoutProgress.Contracts.ReportingIntegration.ReportSubmissionAcceptedProgressEvent;
using WorkoutAcceptedProgressIdempotencyKeys = LgymApi.Application.WorkoutProgress.Contracts.ReportingIntegration.ReportSubmissionAcceptedProgressIdempotencyKeys;
using WorkoutAcceptedMeasurement = LgymApi.Application.WorkoutProgress.Contracts.ReportingIntegration.ReportSubmissionAcceptedMeasurement;

namespace LgymApi.UnitTests;

[TestFixture]
public sealed class ReportSubmissionAcceptedProgressContractTests
{
    private static readonly string[] EventFieldNames =
    [
        nameof(ReportSubmissionAcceptedProgressPayload.SchemaVersion),
        nameof(ReportSubmissionAcceptedProgressPayload.EventId),
        nameof(ReportSubmissionAcceptedProgressPayload.ReportSubmissionId),
        nameof(ReportSubmissionAcceptedProgressPayload.CorrelationId),
        nameof(ReportSubmissionAcceptedProgressPayload.CausationId),
        nameof(ReportSubmissionAcceptedProgressPayload.TraineeId),
        nameof(ReportSubmissionAcceptedProgressPayload.ObservedAt),
        nameof(ReportSubmissionAcceptedProgressPayload.AcceptedAt),
        nameof(ReportSubmissionAcceptedProgressPayload.Measurements)
    ];

    [Test]
    public void Payload_ExposesOnlyPrivacyMinimizedFieldsAndSerializesStably()
    {
        var payload = CreateValidPayload();

        GetOrderedPropertyNames<ReportSubmissionAcceptedProgressPayload>().Should().Equal(EventFieldNames);
        JsonSerializer.Serialize(payload, SharedSerializationOptions.Current).Should().Be(
            "{\"schemaVersion\":1,\"eventId\":\"00000000-0000-0000-0000-000000000001\",\"reportSubmissionId\":\"00000000-0000-0000-0000-000000000002\",\"correlationId\":\"00000000-0000-0000-0000-000000000003\",\"causationId\":\"00000000-0000-0000-0000-000000000004\",\"traineeId\":\"00000000-0000-0000-0000-000000000005\",\"observedAt\":\"2026-07-20T08:30:00+00:00\",\"acceptedAt\":\"2026-07-20T08:31:00+00:00\",\"measurements\":[{\"bodyPart\":\"Chest\",\"value\":101.5,\"unit\":\"Centimeters\"}]}");
    }

    [Test]
    public void Payload_UsesStableSchemaVersionOne()
    {
        ReportSubmissionAcceptedProgressPayload.CurrentSchemaVersion.Should().Be(1);
        CreateValidPayload().SchemaVersion.Should().Be(ReportSubmissionAcceptedProgressPayload.CurrentSchemaVersion);
    }

    [Test]
    public void Command_NestsTheValidatedEventAndRejectsMissingPayload()
    {
        var payload = CreateValidPayload();
        var command = new ReportSubmissionAcceptedProgressCommand { Event = payload };

        command.Validate().IsValid.Should().BeTrue();
        JsonSerializer.Serialize(command, SharedSerializationOptions.Current).Should().Be(
            $"{{\"event\":{JsonSerializer.Serialize(payload, SharedSerializationOptions.Current)}}}");
        var deserializeMissingEvent = () => JsonSerializer.Deserialize<ReportSubmissionAcceptedProgressCommand>(
            "{}",
            SharedSerializationOptions.Current);

        deserializeMissingEvent.Should().Throw<JsonException>();
    }

    [Test]
    public void IdempotencyKeys_AreDeterministicAndMeasurementKeyUsesCanonicalObservedInstant()
    {
        var @event = CreateValidWorkoutEvent();
        var sameInstantWithOffset = @event with { ObservedAt = new DateTimeOffset(2026, 7, 20, 10, 30, 0, TimeSpan.FromHours(2)) };

        WorkoutAcceptedProgressIdempotencyKeys.CreateEventKey(@event)
            .Should().Be("report-submission-accepted-progress:1:event:00000000-0000-0000-0000-000000000001");
        WorkoutAcceptedProgressIdempotencyKeys.CreateEventKey(@event)
            .Should().Be(WorkoutAcceptedProgressIdempotencyKeys.CreateEventKey(@event));
        WorkoutAcceptedProgressIdempotencyKeys.CreateMeasurementKey(@event, @event.Measurements.Single())
            .Should().Be(WorkoutAcceptedProgressIdempotencyKeys.CreateMeasurementKey(sameInstantWithOffset, sameInstantWithOffset.Measurements.Single()));
    }

    [Test]
    public void ConsumeResult_ModelsDuplicateDeliveryAsSuccessfulNoOpAndPoisonOutcomesAsBounded()
    {
        var duplicate = ReportSubmissionAcceptedProgressConsumeResult.Duplicate();
        var poison = ReportSubmissionAcceptedProgressConsumeResult.Poison("unrecoverable payload");

        duplicate.Outcome.Should().Be(ReportSubmissionAcceptedProgressConsumeOutcome.Duplicate);
        duplicate.IsSuccess.Should().BeTrue();
        duplicate.IsNoOp.Should().BeTrue();
        duplicate.RequiresPoisonHandling.Should().BeFalse();
        poison.IsSuccess.Should().BeFalse();
        poison.RequiresPoisonHandling.Should().BeTrue();
        Enum.GetValues<ReportSubmissionAcceptedProgressConsumeOutcome>().Select(outcome => (int)outcome)
            .Should().Equal(0, 1, 2, 3, 4);
    }

    [Test]
    public void Payload_RejectsMalformedOrEmptyStableIdentifiers()
    {
        var payload = CreateValidPayload();
        var validations = new[]
        {
            payload with { EventId = "not-an-id" },
            payload with { EventId = string.Empty },
            payload with { ReportSubmissionId = "not-an-id" },
            payload with { CorrelationId = "not-an-id" },
            payload with { CausationId = "not-an-id" },
            payload with { TraineeId = default }
        }.Select(invalidPayload => invalidPayload.Validate());

        validations.Should().OnlyContain(validation =>
            validation.Outcome == ReportSubmissionAcceptedProgressPayloadValidationOutcome.Invalid);
    }

    [TestCase(double.NaN)]
    [TestCase(0d)]
    [TestCase(-1d)]
    public void Payload_RejectsInvalidMeasurementValues(double value)
    {
        var invalidMeasurement = new ReportSubmissionAcceptedProgressMeasurement(BodyParts.Chest, value, MeasurementUnits.Centimeters);
        var validation = (CreateValidPayload() with { Measurements = [invalidMeasurement] }).Validate();

        validation.Outcome.Should().Be(ReportSubmissionAcceptedProgressPayloadValidationOutcome.Invalid);
    }

    [Test]
    public void Payload_RejectsUnsupportedSchemaVersion()
    {
        var validation = (CreateValidPayload() with { SchemaVersion = 2 }).Validate();

        validation.Outcome.Should().Be(ReportSubmissionAcceptedProgressPayloadValidationOutcome.UnsupportedSchema);
    }

    private static ReportSubmissionAcceptedProgressPayload CreateValidPayload()
    {
        return new ReportSubmissionAcceptedProgressPayload(
            1,
            "00000000-0000-0000-0000-000000000001",
            "00000000-0000-0000-0000-000000000002",
            "00000000-0000-0000-0000-000000000003",
            "00000000-0000-0000-0000-000000000004",
            ParseId<AccountReference>("00000000-0000-0000-0000-000000000005"),
            new DateTimeOffset(2026, 7, 20, 8, 30, 0, TimeSpan.Zero),
            new DateTimeOffset(2026, 7, 20, 8, 31, 0, TimeSpan.Zero),
            [new ReportSubmissionAcceptedProgressMeasurement(BodyParts.Chest, 101.5, MeasurementUnits.Centimeters)]);
    }

    private static WorkoutAcceptedProgressEvent CreateValidWorkoutEvent()
    {
        var payload = CreateValidPayload();
        return new WorkoutAcceptedProgressEvent(
            payload.SchemaVersion,
            payload.EventId,
            payload.ReportSubmissionId,
            payload.CorrelationId,
            payload.CausationId,
            payload.TraineeId,
            payload.ObservedAt,
            payload.AcceptedAt,
            payload.Measurements
                .Select(measurement => new WorkoutAcceptedMeasurement(
                    measurement.BodyPart,
                    measurement.Value,
                    measurement.Unit))
                .ToArray());
    }

    private static string[] GetOrderedPropertyNames<T>()
    {
        return typeof(T)
            .GetProperties(BindingFlags.Instance | BindingFlags.Public | BindingFlags.DeclaredOnly)
            .OrderBy(property => property.MetadataToken)
            .Select(property => property.Name)
            .ToArray();
    }

    private static Id<TEntity> ParseId<TEntity>(string value)
        where TEntity : class
    {
        Id<TEntity>.TryParse(value, out var id).Should().BeTrue();
        return id;
    }
}
