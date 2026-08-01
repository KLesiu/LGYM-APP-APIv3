using FluentAssertions;
using LgymApi.Application.Repositories;
using LgymApi.Application.WorkoutProgress.Contracts.ReportingIntegration;
using LgymApi.Application.WorkoutProgress.ReportingIntegration;
using LgymApi.Domain.Enums;
using LgymApi.Domain.ValueObjects;
using LgymApi.Identity.Contracts;
using NSubstitute;

namespace LgymApi.UnitTests;

[TestFixture]
public sealed class ReportSubmissionAcceptedProgressConsumerTests
{
    [Test]
    public async Task ConsumeAsync_WithValidEvent_StagesOneMeasurementPerNewBodyPartAndCommitsOnce()
    {
        var persistence = Substitute.For<IReportSubmissionAcceptedProgressPersistence>();
        var unitOfWork = Substitute.For<IUnitOfWork>();
        var staged = new List<AcceptedReportMeasurementPersistenceModel>();
        var @event = CreateValidEvent(
            new ReportSubmissionAcceptedMeasurement(BodyParts.Chest, 101.5, MeasurementUnits.Centimeters),
            new ReportSubmissionAcceptedMeasurement(BodyParts.BodyWeight, 82.4, MeasurementUnits.Kilograms));
        ConfigureExisting(persistence);
        ConfigureStaging(persistence, staged);
        var consumer = new ReportSubmissionAcceptedProgressConsumer(persistence, unitOfWork);

        var result = await consumer.ConsumeAsync(@event);

        result.Outcome.Should().Be(ReportSubmissionAcceptedProgressConsumeOutcome.Applied);
        staged.Select(measurement => measurement.BodyPart).Should().BeEquivalentTo([BodyParts.Chest, BodyParts.BodyWeight]);
        staged.Should().OnlyContain(measurement => measurement.TraineeId == @event.TraineeId && measurement.CreatedAt == @event.ObservedAt);
        await persistence.Received(1).GetExistingBodyPartsAsync(
            @event.TraineeId,
            Arg.Is<IReadOnlyCollection<BodyParts>>(bodyParts => bodyParts.ToHashSet().SetEquals(new[] { BodyParts.Chest, BodyParts.BodyWeight })),
            new DateTimeOffset(2026, 7, 20, 0, 0, 0, TimeSpan.Zero),
            new DateTimeOffset(2026, 7, 21, 0, 0, 0, TimeSpan.Zero),
            Arg.Any<CancellationToken>());
        await unitOfWork.Received(1).SaveChangesAsync(Arg.Any<CancellationToken>());
    }

    [Test]
    public async Task ConsumeAsync_WithReplay_ReturnsDuplicateWithoutSecondCommit()
    {
        var persistence = Substitute.For<IReportSubmissionAcceptedProgressPersistence>();
        var unitOfWork = Substitute.For<IUnitOfWork>();
        var existing = new HashSet<BodyParts>();
        var staged = new List<AcceptedReportMeasurementPersistenceModel>();
        var @event = CreateValidEvent(new ReportSubmissionAcceptedMeasurement(BodyParts.Chest, 101.5, MeasurementUnits.Centimeters));
        persistence.GetExistingBodyPartsAsync(
                Arg.Any<Id<AccountReference>>(),
                Arg.Any<IReadOnlyCollection<BodyParts>>(),
                Arg.Any<DateTimeOffset>(),
                Arg.Any<DateTimeOffset>(),
                Arg.Any<CancellationToken>())
            .Returns(_ => Task.FromResult<IReadOnlySet<BodyParts>>(existing.ToHashSet()));
        persistence.AddAsync(Arg.Do<AcceptedReportMeasurementPersistenceModel>(measurement =>
            {
                staged.Add(measurement);
                existing.Add(measurement.BodyPart);
            }), Arg.Any<CancellationToken>())
            .Returns(Task.CompletedTask);
        var consumer = new ReportSubmissionAcceptedProgressConsumer(persistence, unitOfWork);

        (await consumer.ConsumeAsync(@event)).Outcome.Should().Be(ReportSubmissionAcceptedProgressConsumeOutcome.Applied);
        (await consumer.ConsumeAsync(@event)).Outcome.Should().Be(ReportSubmissionAcceptedProgressConsumeOutcome.Duplicate);

        staged.Should().ContainSingle();
        await unitOfWork.Received(1).SaveChangesAsync(Arg.Any<CancellationToken>());
    }

    [Test]
    public async Task ConsumeAsync_WithDuplicateBodyPartForTheSameUtcDay_StagesTheFirstMeasurementOnce()
    {
        var persistence = Substitute.For<IReportSubmissionAcceptedProgressPersistence>();
        var unitOfWork = Substitute.For<IUnitOfWork>();
        var staged = new List<AcceptedReportMeasurementPersistenceModel>();
        var @event = CreateValidEvent(
            new ReportSubmissionAcceptedMeasurement(BodyParts.Chest, 101.5, MeasurementUnits.Centimeters),
            new ReportSubmissionAcceptedMeasurement(BodyParts.Chest, 99.1, MeasurementUnits.Centimeters));
        ConfigureExisting(persistence);
        ConfigureStaging(persistence, staged);
        var consumer = new ReportSubmissionAcceptedProgressConsumer(persistence, unitOfWork);

        var result = await consumer.ConsumeAsync(@event);

        result.Outcome.Should().Be(ReportSubmissionAcceptedProgressConsumeOutcome.Applied);
        staged.Should().ContainSingle(measurement =>
            measurement.BodyPart == BodyParts.Chest && measurement.Value == 101.5);
        await unitOfWork.Received(1).SaveChangesAsync(Arg.Any<CancellationToken>());
    }

    [Test]
    public async Task ConsumeAsync_WithPartialReplay_StagesOnlyMissingBodyParts()
    {
        var persistence = Substitute.For<IReportSubmissionAcceptedProgressPersistence>();
        var unitOfWork = Substitute.For<IUnitOfWork>();
        var staged = new List<AcceptedReportMeasurementPersistenceModel>();
        var @event = CreateValidEvent(
            new ReportSubmissionAcceptedMeasurement(BodyParts.Chest, 101.5, MeasurementUnits.Centimeters),
            new ReportSubmissionAcceptedMeasurement(BodyParts.Waist, 87.1, MeasurementUnits.Centimeters));
        ConfigureExisting(persistence, BodyParts.Chest);
        ConfigureStaging(persistence, staged);
        var consumer = new ReportSubmissionAcceptedProgressConsumer(persistence, unitOfWork);

        var result = await consumer.ConsumeAsync(@event);

        result.Outcome.Should().Be(ReportSubmissionAcceptedProgressConsumeOutcome.Applied);
        staged.Should().ContainSingle(measurement => measurement.BodyPart == BodyParts.Waist);
    }

    [TestCase(0, ReportSubmissionAcceptedProgressConsumeOutcome.UnsupportedSchema)]
    [TestCase(2, ReportSubmissionAcceptedProgressConsumeOutcome.UnsupportedSchema)]
    public async Task ConsumeAsync_WithRejectedEvent_DoesNotPersistOrCommit(
        int schemaVersion,
        ReportSubmissionAcceptedProgressConsumeOutcome expectedOutcome)
    {
        var persistence = Substitute.For<IReportSubmissionAcceptedProgressPersistence>();
        var unitOfWork = Substitute.For<IUnitOfWork>();
        var @event = CreateValidEvent(new ReportSubmissionAcceptedMeasurement(BodyParts.Chest, 101.5, MeasurementUnits.Centimeters)) with
        {
            SchemaVersion = schemaVersion
        };
        var consumer = new ReportSubmissionAcceptedProgressConsumer(persistence, unitOfWork);

        var result = await consumer.ConsumeAsync(@event);

        result.Outcome.Should().Be(expectedOutcome);
        await persistence.DidNotReceiveWithAnyArgs().AddAsync(default!, default);
        await unitOfWork.DidNotReceiveWithAnyArgs().SaveChangesAsync(default);
    }

    [Test]
    public async Task ConsumeAsync_WhenPersistenceThrows_PropagatesTransientFailure()
    {
        var persistence = Substitute.For<IReportSubmissionAcceptedProgressPersistence>();
        var unitOfWork = Substitute.For<IUnitOfWork>();
        var @event = CreateValidEvent(new ReportSubmissionAcceptedMeasurement(BodyParts.Chest, 101.5, MeasurementUnits.Centimeters));
        var exception = new TimeoutException("Transient measurement persistence failure.");
        persistence.GetExistingBodyPartsAsync(
                Arg.Any<Id<AccountReference>>(),
                Arg.Any<IReadOnlyCollection<BodyParts>>(),
                Arg.Any<DateTimeOffset>(),
                Arg.Any<DateTimeOffset>(),
                Arg.Any<CancellationToken>())
            .Returns(Task.FromException<IReadOnlySet<BodyParts>>(exception));
        var consumer = new ReportSubmissionAcceptedProgressConsumer(persistence, unitOfWork);

        var action = () => consumer.ConsumeAsync(@event);

        await action.Should().ThrowAsync<TimeoutException>().WithMessage(exception.Message);
        await unitOfWork.DidNotReceiveWithAnyArgs().SaveChangesAsync(default);
    }

    private static void ConfigureExisting(
        IReportSubmissionAcceptedProgressPersistence persistence,
        params BodyParts[] existing)
        => persistence.GetExistingBodyPartsAsync(
                Arg.Any<Id<AccountReference>>(),
                Arg.Any<IReadOnlyCollection<BodyParts>>(),
                Arg.Any<DateTimeOffset>(),
                Arg.Any<DateTimeOffset>(),
                Arg.Any<CancellationToken>())
            .Returns(Task.FromResult<IReadOnlySet<BodyParts>>(existing.ToHashSet()));

    private static void ConfigureStaging(
        IReportSubmissionAcceptedProgressPersistence persistence,
        ICollection<AcceptedReportMeasurementPersistenceModel> staged)
        => persistence.AddAsync(Arg.Do<AcceptedReportMeasurementPersistenceModel>(staged.Add), Arg.Any<CancellationToken>())
            .Returns(Task.CompletedTask);

    private static ReportSubmissionAcceptedProgressEvent CreateValidEvent(params ReportSubmissionAcceptedMeasurement[] measurements)
    {
        Id<AccountReference>.TryParse("00000000-0000-0000-0000-000000000005", out var traineeId).Should().BeTrue();
        return new ReportSubmissionAcceptedProgressEvent(
            ReportSubmissionAcceptedProgressEvent.CurrentSchemaVersion,
            "00000000-0000-0000-0000-000000000001",
            "00000000-0000-0000-0000-000000000002",
            "00000000-0000-0000-0000-000000000003",
            "00000000-0000-0000-0000-000000000004",
            traineeId,
            new DateTimeOffset(2026, 7, 20, 10, 30, 0, TimeSpan.Zero),
            new DateTimeOffset(2026, 7, 20, 10, 31, 0, TimeSpan.Zero),
            measurements);
    }
}
