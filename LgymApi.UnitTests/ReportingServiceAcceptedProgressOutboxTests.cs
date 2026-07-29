using System.Text.Json;
using FluentAssertions;
using LgymApi.Application.Abstractions.Storage;
using LgymApi.Application.Features.Reporting;
using LgymApi.Application.Features.Reporting.Models;
using LgymApi.Application.Options;
using LgymApi.Application.Platform.Contracts.BackgroundCommands;
using LgymApi.Application.Reporting.Contracts.BackgroundCommands;
using LgymApi.Application.Reporting.Persistence;
using LgymApi.Application.Repositories;
using LgymApi.Domain.Entities;
using LgymApi.Domain.Enums;
using LgymApi.Domain.ValueObjects;
using LgymApi.Identity.Contracts;
using Microsoft.Extensions.Logging;
using NSubstitute;

namespace LgymApi.UnitTests;

[TestFixture]
public sealed class ReportingServiceAcceptedProgressOutboxTests
{
    [Test]
    public async Task Submit_StagesValidMeasurementsBeforeCommitAndEnqueuesNotificationAfterCommit()
    {
        var traineeId = Id<AccountReference>.New();
        var trainerId = Id<AccountReference>.New();
        var requestId = Id<ReportRequest>.New();
        var template = CreateTemplate(
            CreateMeasurementsField("first", """{ "measurementTypes": ["weight", "chest", "waist", "bodyFat"] }"""),
            CreateMeasurementsField("later", """{ "measurementTypes": ["bodyWeight", "thighs"] }"""));
        var request = CreateRequest(requestId, traineeId, trainerId, template);
        var persistence = Substitute.For<IReportRequestSubmissionPersistence>();
        var unitOfWork = Substitute.For<IUnitOfWork>();
        var dispatcher = Substitute.For<ICommandDispatcher>();
        var outbox = Substitute.For<ICommandOutboxWriter>();
        NewReportSubmissionPersistenceModel? addedSubmission = null;
        var stagedBeforeCommit = false;
        var committed = false;
        persistence.FindRequestByIdAsync(requestId, Arg.Any<CancellationToken>()).Returns(request);
        persistence.AddSubmissionAsync(Arg.Do<NewReportSubmissionPersistenceModel>(submission => addedSubmission = submission), Arg.Any<CancellationToken>())
            .Returns(Task.CompletedTask);
        outbox.StageAsync(Arg.Any<ReportSubmissionAcceptedProgressCommand>(), Arg.Any<CancellationToken>())
            .Returns(_ =>
            {
                stagedBeforeCommit = true;
                return Task.FromResult(new CommandEnvelopeStageResult(null, false));
            });
        unitOfWork.SaveChangesAsync(Arg.Any<CancellationToken>()).Returns(_ =>
        {
            stagedBeforeCommit.Should().BeTrue();
            committed = true;
            return Task.FromResult(1);
        });
        dispatcher.EnqueueAsync(Arg.Any<ReportSubmissionCreatedInAppNotificationCommand>()).Returns(_ =>
        {
            committed.Should().BeTrue();
            return Task.CompletedTask;
        });
        var service = CreateService(persistence, unitOfWork, dispatcher, outbox);

        var result = await service.SubmitReportRequestAsync(
            ReportingTestData.Account(traineeId.Rebind<User>()),
            requestId,
            new SubmitReportRequestCommand
            {
                Answers = new Dictionary<string, JsonElement>
                {
                    ["first"] = ParseJson("""
                        {
                          "weight": { "value": 82.4, "unit": "Kilograms" },
                          "chest": { "value": 101.2, "unit": "Centimeters" },
                          "waist": { "value": 87.1, "unit": "Kilograms" },
                          "bodyFat": { "value": 0, "unit": "Percentages" }
                        }
                        """),
                    ["later"] = ParseJson("""
                        {
                          "bodyWeight": { "value": 81.7, "unit": "Kilograms" },
                          "thighs": { "value": 60.0, "unit": "Centimeters" }
                        }
                        """)
                }
            });

        result.IsSuccess.Should().BeTrue();
        addedSubmission.Should().NotBeNull();
        await outbox.Received(1).StageAsync(Arg.Is<ReportSubmissionAcceptedProgressCommand>(command =>
            command.Event.Validate().IsValid
            && command.Event.ReportSubmissionId == addedSubmission!.Id.ToString()
            && command.Event.CorrelationId == requestId.ToString()
            && command.Event.TraineeId == traineeId
            && command.Event.Measurements.SequenceEqual(new[]
            {
                new ReportSubmissionAcceptedProgressMeasurement(BodyParts.BodyWeight, 82.4, MeasurementUnits.Kilograms),
                new ReportSubmissionAcceptedProgressMeasurement(BodyParts.Chest, 101.2, MeasurementUnits.Centimeters),
                new ReportSubmissionAcceptedProgressMeasurement(BodyParts.Thigh, 60.0, MeasurementUnits.Centimeters)
            })), Arg.Any<CancellationToken>());
        await dispatcher.Received(1).EnqueueAsync(Arg.Is<ReportSubmissionCreatedInAppNotificationCommand>(command =>
            command.SubmissionId == addedSubmission.Id
            && command.TrainerId == trainerId
            && command.TraineeId == traineeId));
    }

    [Test]
    public async Task Submit_WithNoValidMeasurements_DoesNotStageAcceptedProgress()
    {
        var traineeId = Id<AccountReference>.New();
        var requestId = Id<ReportRequest>.New();
        var template = CreateTemplate(new ReportTemplateFieldPersistenceModel(
            Id<ReportTemplateField>.New(), "feedback", "Feedback", ReportFieldType.Text, true, 1, null, DateTimeOffset.UtcNow));
        var persistence = Substitute.For<IReportRequestSubmissionPersistence>();
        persistence.FindRequestByIdAsync(requestId, Arg.Any<CancellationToken>())
            .Returns(CreateRequest(requestId, traineeId, Id<AccountReference>.New(), template));
        var outbox = Substitute.For<ICommandOutboxWriter>();
        var service = CreateService(persistence, Substitute.For<IUnitOfWork>(), Substitute.For<ICommandDispatcher>(), outbox);

        var result = await service.SubmitReportRequestAsync(
            ReportingTestData.Account(traineeId.Rebind<User>()),
            requestId,
            new SubmitReportRequestCommand { Answers = new() { ["feedback"] = ParseJson("\"complete\"") } });

        result.IsSuccess.Should().BeTrue();
        await outbox.DidNotReceive().StageAsync(Arg.Any<ReportSubmissionAcceptedProgressCommand>(), Arg.Any<CancellationToken>());
    }

    [Test]
    public async Task Submit_WhenOutboxStagingFails_DoesNotCommitOrNotify()
    {
        var traineeId = Id<AccountReference>.New();
        var requestId = Id<ReportRequest>.New();
        var template = CreateTemplate(CreateMeasurementsField("measurements", """{ "measurementTypes": ["weight"] }"""));
        var persistence = Substitute.For<IReportRequestSubmissionPersistence>();
        persistence.FindRequestByIdAsync(requestId, Arg.Any<CancellationToken>())
            .Returns(CreateRequest(requestId, traineeId, Id<AccountReference>.New(), template));
        var unitOfWork = Substitute.For<IUnitOfWork>();
        var dispatcher = Substitute.For<ICommandDispatcher>();
        var outbox = Substitute.For<ICommandOutboxWriter>();
        outbox.StageAsync(Arg.Any<ReportSubmissionAcceptedProgressCommand>(), Arg.Any<CancellationToken>())
            .Returns(Task.FromException<CommandEnvelopeStageResult>(new InvalidOperationException("Outbox staging failed.")));
        var service = CreateService(persistence, unitOfWork, dispatcher, outbox);

        var action = () => service.SubmitReportRequestAsync(
            ReportingTestData.Account(traineeId.Rebind<User>()),
            requestId,
            new SubmitReportRequestCommand
            {
                Answers = new() { ["measurements"] = ParseJson("""{ "weight": { "value": 82.4, "unit": "Kilograms" } }""") }
            });

        await action.Should().ThrowAsync<InvalidOperationException>();
        await unitOfWork.DidNotReceiveWithAnyArgs().SaveChangesAsync(default);
        await dispatcher.DidNotReceive().EnqueueAsync(Arg.Any<ReportSubmissionCreatedInAppNotificationCommand>());
    }

    private static ReportingService CreateService(
        IReportRequestSubmissionPersistence persistence,
        IUnitOfWork unitOfWork,
        ICommandDispatcher dispatcher,
        ICommandOutboxWriter outbox)
    {
        var dependencies = Substitute.For<IReportingServiceDependencies>();
        dependencies.TemplatePersistence.Returns(Substitute.For<IReportTemplatePersistence>());
        dependencies.RequestSubmissionPersistence.Returns(persistence);
        dependencies.RecurringAssignmentPersistence.Returns(Substitute.For<IRecurringReportAssignmentPersistence>());
        dependencies.PhotoPersistence.Returns(Substitute.For<IReportPhotoPersistence>());
        dependencies.RelationshipAccessPersistence.Returns(Substitute.For<IReportingRelationshipAccessPersistence>());
        dependencies.ReportSubmissionAcceptedProgressCommandFactory.Returns(new ReportSubmissionAcceptedProgressCommandFactory());
        dependencies.CommandDispatcher.Returns(dispatcher);
        dependencies.CommandOutboxWriter.Returns(outbox);
        dependencies.UnitOfWork.Returns(unitOfWork);
        dependencies.PhotoStorageProvider.Returns(Substitute.For<IPhotoStorageProvider>());
        dependencies.Mapper.Returns(ReportingTestData.Mapper());
        dependencies.Logger.Returns(Substitute.For<ILogger<ReportingService>>());
        dependencies.PhotoStorageOptions.Returns(new PhotoStorageOptions());
        return new ReportingService(dependencies);
    }

    private static ReportTemplatePersistenceModel CreateTemplate(params ReportTemplateFieldPersistenceModel[] fields)
        => new(Id<ReportTemplate>.New(), Id<AccountReference>.New(), "Progress check-in", null, DateTimeOffset.UtcNow, false, fields);

    private static ReportTemplateFieldPersistenceModel CreateMeasurementsField(string key, string moduleConfig)
        => new(Id<ReportTemplateField>.New(), key, key, ReportFieldType.Measurements, false, 2, moduleConfig, DateTimeOffset.UtcNow);

    private static ReportRequestPersistenceModel CreateRequest(
        Id<ReportRequest> requestId,
        Id<AccountReference> traineeId,
        Id<AccountReference> trainerId,
        ReportTemplatePersistenceModel template)
        => new(requestId, trainerId, traineeId, template.Id, null, ReportRequestStatus.Pending, null, null, null, DateTimeOffset.UtcNow, false, template, null);

    private static JsonElement ParseJson(string json) => JsonDocument.Parse(json).RootElement.Clone();
}
