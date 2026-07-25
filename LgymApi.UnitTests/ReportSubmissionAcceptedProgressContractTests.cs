using System.Reflection;
using System.Text.Json;
using FluentAssertions;
using LgymApi.Application.Platform.Contracts.Serialization;
using LgymApi.Application.Reporting.Contracts.BackgroundCommands;
using LgymApi.Application.WorkoutProgress.Contracts.ReportingIntegration;
using LgymApi.Domain.Entities;
using LgymApi.Domain.Enums;
using LgymApi.Domain.ValueObjects;

namespace LgymApi.UnitTests;

[TestFixture]
public sealed class ReportSubmissionAcceptedProgressContractTests
{
    private static readonly string[] EventFieldNames =
    [
        nameof(ReportSubmissionAcceptedProgressEvent.SchemaVersion),
        nameof(ReportSubmissionAcceptedProgressEvent.EventId),
        nameof(ReportSubmissionAcceptedProgressEvent.ReportSubmissionId),
        nameof(ReportSubmissionAcceptedProgressEvent.CorrelationId),
        nameof(ReportSubmissionAcceptedProgressEvent.CausationId),
        nameof(ReportSubmissionAcceptedProgressEvent.TraineeId),
        nameof(ReportSubmissionAcceptedProgressEvent.ObservedAt),
        nameof(ReportSubmissionAcceptedProgressEvent.AcceptedAt),
        nameof(ReportSubmissionAcceptedProgressEvent.Measurements)
    ];

    [Test]
    public void Event_ExposesOnlyPrivacyMinimizedFieldsAndSerializesStably()
    {
        var @event = CreateValidEvent();

        GetOrderedPropertyNames<ReportSubmissionAcceptedProgressEvent>().Should().Equal(EventFieldNames);
        JsonSerializer.Serialize(@event, SharedSerializationOptions.Current).Should().Be(
            "{\"schemaVersion\":1,\"eventId\":\"00000000-0000-0000-0000-000000000001\",\"reportSubmissionId\":\"00000000-0000-0000-0000-000000000002\",\"correlationId\":\"00000000-0000-0000-0000-000000000003\",\"causationId\":\"00000000-0000-0000-0000-000000000004\",\"traineeId\":\"00000000-0000-0000-0000-000000000005\",\"observedAt\":\"2026-07-20T08:30:00+00:00\",\"acceptedAt\":\"2026-07-20T08:31:00+00:00\",\"measurements\":[{\"bodyPart\":\"Chest\",\"value\":101.5,\"unit\":\"Centimeters\"}]}");
    }

    [Test]
    public void Event_UsesStableSchemaVersionOne()
    {
        ReportSubmissionAcceptedProgressEvent.CurrentSchemaVersion.Should().Be(1);
        CreateValidEvent().SchemaVersion.Should().Be(ReportSubmissionAcceptedProgressEvent.CurrentSchemaVersion);
    }

    [Test]
    public void Command_NestsTheValidatedEventAndRejectsMissingPayload()
    {
        var @event = CreateValidEvent();
        var command = new ReportSubmissionAcceptedProgressCommand { Event = @event };

        command.Validate().IsValid.Should().BeTrue();
        JsonSerializer.Serialize(command, SharedSerializationOptions.Current).Should().Be(
            $"{{\"event\":{JsonSerializer.Serialize(@event, SharedSerializationOptions.Current)}}}");
        var deserializeMissingEvent = () => JsonSerializer.Deserialize<ReportSubmissionAcceptedProgressCommand>(
            "{}",
            SharedSerializationOptions.Current);

        deserializeMissingEvent.Should().Throw<JsonException>();
    }

    [Test]
    public void IdempotencyKeys_AreDeterministicAndMeasurementKeyUsesCanonicalObservedInstant()
    {
        var @event = CreateValidEvent();
        var sameInstantWithOffset = @event with { ObservedAt = new DateTimeOffset(2026, 7, 20, 10, 30, 0, TimeSpan.FromHours(2)) };

        ReportSubmissionAcceptedProgressIdempotencyKeys.CreateEventKey(@event)
            .Should().Be("report-submission-accepted-progress:1:event:00000000-0000-0000-0000-000000000001");
        ReportSubmissionAcceptedProgressIdempotencyKeys.CreateEventKey(@event)
            .Should().Be(ReportSubmissionAcceptedProgressIdempotencyKeys.CreateEventKey(@event));
        ReportSubmissionAcceptedProgressIdempotencyKeys.CreateMeasurementKey(@event, @event.Measurements.Single())
            .Should().Be(ReportSubmissionAcceptedProgressIdempotencyKeys.CreateMeasurementKey(sameInstantWithOffset, sameInstantWithOffset.Measurements.Single()));
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
    public void Event_RejectsMalformedOrEmptyStableIdentifiers()
    {
        var @event = CreateValidEvent();
        var validations = new[]
        {
            @event with { EventId = "not-an-id" },
            @event with { EventId = string.Empty },
            @event with { ReportSubmissionId = "not-an-id" },
            @event with { CorrelationId = "not-an-id" },
            @event with { CausationId = "not-an-id" },
            @event with { TraineeId = default }
        }.Select(invalidEvent => invalidEvent.Validate());

        validations.Should().OnlyContain(validation =>
            validation.Outcome == ReportSubmissionAcceptedProgressValidationOutcome.Invalid);
    }

    [TestCase(double.NaN)]
    [TestCase(0d)]
    [TestCase(-1d)]
    public void Event_RejectsInvalidMeasurementValues(double value)
    {
        var invalidMeasurement = new ReportSubmissionAcceptedMeasurement(BodyParts.Chest, value, MeasurementUnits.Centimeters);
        var validation = (CreateValidEvent() with { Measurements = [invalidMeasurement] }).Validate();

        validation.Outcome.Should().Be(ReportSubmissionAcceptedProgressValidationOutcome.Invalid);
    }

    [Test]
    public void Event_RejectsUnsupportedSchemaVersion()
    {
        var validation = (CreateValidEvent() with { SchemaVersion = 2 }).Validate();

        validation.Outcome.Should().Be(ReportSubmissionAcceptedProgressValidationOutcome.UnsupportedSchema);
    }

    private static ReportSubmissionAcceptedProgressEvent CreateValidEvent()
    {
        return new ReportSubmissionAcceptedProgressEvent(
            1,
            "00000000-0000-0000-0000-000000000001",
            "00000000-0000-0000-0000-000000000002",
            "00000000-0000-0000-0000-000000000003",
            "00000000-0000-0000-0000-000000000004",
            ParseId<User>("00000000-0000-0000-0000-000000000005"),
            new DateTimeOffset(2026, 7, 20, 8, 30, 0, TimeSpan.Zero),
            new DateTimeOffset(2026, 7, 20, 8, 31, 0, TimeSpan.Zero),
            [new ReportSubmissionAcceptedMeasurement(BodyParts.Chest, 101.5, MeasurementUnits.Centimeters)]);
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
