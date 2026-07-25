using System.Reflection;
using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using FluentAssertions;
using LgymApi.Application.BuildingBlocks.Errors;
using LgymApi.Application.Reporting.Errors;
using LgymApi.Application.Features.Reporting;
using LgymApi.Application.Features.Reporting.Models;
using LgymApi.Application.WorkoutProgress.Contracts.ReportingIntegration;
using LgymApi.BackgroundWorker.Common.Jobs;
using LgymApi.Domain.Entities;
using LgymApi.Domain.Enums;
using LgymApi.Domain.ValueObjects;
using LgymApi.Infrastructure.Data;
using LgymApi.Infrastructure.UnitOfWork;
using LgymApi.Resources;
using LgymApi.TestUtils;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using NUnit.Framework;

namespace LgymApi.IntegrationTests;

[TestFixture]
[Category("PostgreSql")]
internal sealed class PostgreSqlReportingProgressOutboxTests : PostgreSqlIntegrationTestBase
{
    private const string AcceptedProgressCommandId =
        "LgymApi.BackgroundWorker.Common.Commands.ReportSubmissionAcceptedProgressCommand";

    private const string AcceptedProgressCommandTypeName =
        "LgymApi.Application.Reporting.Contracts.BackgroundCommands.ReportSubmissionAcceptedProgressCommand";

    private const string CommandOutboxWriterTypeName =
        "LgymApi.Application.Platform.Contracts.BackgroundCommands.ICommandOutboxWriter";

    [Test]
    public async Task SubmitReportRequestAsync_WithMeasurements_CommitsSubmissionRequestStateAndAcceptedProgressEnvelopeTogether()
    {
        var scenario = await CreatePendingMeasurementScenarioAsync();

        await using (var scope = Factory.Services.CreateAsyncScope())
        {
            var reportingService = scope.ServiceProvider.GetRequiredService<IReportingService>();
            var result = await reportingService.SubmitReportRequestAsync(
                scenario.Trainee,
                scenario.RequestId,
                CreateMeasurementSubmissionCommand());

            result.IsSuccess.Should().BeTrue();
        }

        var persisted = await ReadSubmissionStateAsync(scenario.RequestId);

        Assert.Multiple(() =>
        {
            persisted.Submission.Should().NotBeNull();
            persisted.RequestStatus.Should().Be(ReportRequestStatus.Submitted);
            persisted.AcceptedProgressEnvelopes.Should().ContainSingle(
                "the accepted-progress command must be staged before the Reporting unit of work commits");
            persisted.AcceptedProgressEnvelopes[0].CommandTypeFullName.Should().Be(AcceptedProgressCommandId);
            persisted.AcceptedProgressEnvelopes[0].PayloadJson.Should().Contain(persisted.Submission!.Id.ToString());
        });
    }

    [Test]
    public async Task SubmitReportRequestAsync_WhenSecondSubmissionIsRejected_DoesNotCreateAnotherAcceptedProgressEnvelope()
    {
        var scenario = await CreatePendingMeasurementScenarioAsync();
        var submissionCommand = CreateMeasurementSubmissionCommand();

        await using (var scope = Factory.Services.CreateAsyncScope())
        {
            var reportingService = scope.ServiceProvider.GetRequiredService<IReportingService>();
            var first = await reportingService.SubmitReportRequestAsync(scenario.Trainee, scenario.RequestId, submissionCommand);
            var envelopeCountAfterFirstSubmission = await CountAcceptedProgressEnvelopesAsync();
            var second = await reportingService.SubmitReportRequestAsync(scenario.Trainee, scenario.RequestId, submissionCommand);
            var envelopeCountAfterDuplicate = await CountAcceptedProgressEnvelopesAsync();

            Assert.Multiple(() =>
            {
                first.IsSuccess.Should().BeTrue();
                envelopeCountAfterFirstSubmission.Should().Be(1);
                second.IsFailure.Should().BeTrue();
                second.Error.Should().BeOfType<InvalidReportingError>();
                second.Error.Message.Should().Be(Messages.ReportRequestNotPending);
                envelopeCountAfterDuplicate.Should().Be(envelopeCountAfterFirstSubmission);
            });
        }
    }

    [Test]
    public async Task CommittedAcceptedProgressEnvelope_IsDispatchedAndDeliveredIdempotentlyWithoutHangfire()
    {
        var scenario = await CreatePendingMeasurementScenarioAsync();

        await using (var submissionScope = Factory.Services.CreateAsyncScope())
        {
            var reportingService = submissionScope.ServiceProvider.GetRequiredService<IReportingService>();
            var result = await reportingService.SubmitReportRequestAsync(
                scenario.Trainee,
                scenario.RequestId,
                CreateMeasurementSubmissionCommand());

            result.IsSuccess.Should().BeTrue();
        }

        var stagedEnvelope = (await ReadSubmissionStateAsync(scenario.RequestId))
            .AcceptedProgressEnvelopes
            .Should()
            .ContainSingle()
            .Subject;

        await using (var dispatchScope = Factory.Services.CreateAsyncScope())
        {
            var dispatchJob = dispatchScope.ServiceProvider.GetRequiredService<ICommittedIntentDispatchJob>();
            await dispatchJob.ExecuteAsync();
        }

        var dispatchedEnvelope = await ReadEnvelopeAsync(stagedEnvelope.Id);
        Assert.Multiple(() =>
        {
            dispatchedEnvelope.DispatchedAt.Should().NotBeNull("the committed intent must be scheduled after its database commit");
            dispatchedEnvelope.SchedulerJobId.Should().StartWith(
                "noop-command-",
                "Testing uses the registered no-op action scheduler instead of a Hangfire server");
            dispatchedEnvelope.Status.Should().Be(ActionExecutionStatus.Pending);
        });

        await using (var deliveryScope = Factory.Services.CreateAsyncScope())
        {
            var job = deliveryScope.ServiceProvider.GetRequiredService<IActionMessageJob>();
            await job.ExecuteAsync(stagedEnvelope.Id);
        }

        await ResetEnvelopeForReplayAsync(stagedEnvelope.Id);

        await using (var replayScope = Factory.Services.CreateAsyncScope())
        {
            var job = replayScope.ServiceProvider.GetRequiredService<IActionMessageJob>();
            await job.ExecuteAsync(stagedEnvelope.Id);
        }

        var delivery = await ReadDeliveryStateAsync(stagedEnvelope.Id, scenario.Trainee.Id);
        Assert.Multiple(() =>
        {
            delivery.Envelope.Status.Should().Be(ActionExecutionStatus.Completed);
            delivery.Measurements.Should().ContainSingle();
            delivery.Measurements[0].BodyPart.Should().Be(BodyParts.BodyWeight);
            delivery.Measurements[0].Value.Should().Be(82.4);
            delivery.Measurements[0].Unit.Should().Be(MeasurementUnits.Kilograms.ToString());
        });
    }

    [Test]
    public async Task SubmitReportRequestApi_WithMeasurements_PreservesLegacySuccessResponseShape()
    {
        var scenario = await CreatePendingMeasurementScenarioAsync();
        await AuthenticateAsAsync(scenario.Trainee);

        var response = await Client.PostAsJsonAsync(
            $"/api/trainee/report-requests/{scenario.RequestId}/submit",
            new
            {
                answers = new
                {
                    measurements = new
                    {
                        BodyWeight = new { value = 82.4, unit = MeasurementUnits.Kilograms.ToString() }
                    }
                }
            });

        response.StatusCode.Should().Be(HttpStatusCode.OK);

        using var responseJson = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        var responseBody = responseJson.RootElement;
        ReadRequiredString(responseBody, "_id").Should().NotBeNullOrWhiteSpace();
        responseBody.TryGetProperty("id", out _).Should().BeFalse("the established submission DTO exposes _id");
        ReadRequiredString(responseBody, "reportRequestId").Should().Be(scenario.RequestId.ToString());
        ReadRequiredString(responseBody, "traineeId").Should().Be(scenario.Trainee.Id.ToString());
        responseBody.TryGetProperty("answers", out var answers).Should().BeTrue();
        answers.ValueKind.Should().Be(JsonValueKind.Object);
        answers.TryGetProperty("measurements", out _).Should().BeTrue();
        responseBody.TryGetProperty("request", out _).Should().BeTrue("the established submission DTO includes its request");
    }

    [Test]
    public async Task ForcedFailure_AfterStagingSubmissionAndAcceptedProgressCommand_RollsBackBothFromAFreshContext()
    {
        var scenario = await CreatePendingMeasurementScenarioAsync();
        var submission = new ReportSubmission
        {
            Id = Id<ReportSubmission>.New(),
            ReportRequestId = scenario.RequestId,
            TraineeId = scenario.Trainee.Id,
            PayloadJson = "{}"
        };

        await using (var scope = Factory.Services.CreateAsyncScope())
        {
            var database = scope.ServiceProvider.GetRequiredService<AppDbContext>();
            var unitOfWork = new EfUnitOfWork(database);
            var request = await database.ReportRequests.SingleAsync(candidate => candidate.Id == scenario.RequestId);

            try
            {
                await using var transaction = await unitOfWork.BeginTransactionAsync();
                request.Status = ReportRequestStatus.Submitted;
                await database.ReportSubmissions.AddAsync(submission);
                await StageAcceptedProgressCommandAsync(scope.ServiceProvider, submission, scenario.Trainee.Id, scenario.RequestId);
                await unitOfWork.SaveChangesAsync();

                throw new ForcedFailureAfterOutboxStagingException();
            }
            catch (ForcedFailureAfterOutboxStagingException)
            {
            }
        }

        var persisted = await ReadSubmissionStateAsync(scenario.RequestId);

        Assert.Multiple(() =>
        {
            persisted.Submission.Should().BeNull();
            persisted.RequestStatus.Should().Be(ReportRequestStatus.Pending);
            persisted.AcceptedProgressEnvelopes.Should().BeEmpty();
        });
    }

    private async Task<PendingMeasurementScenario> CreatePendingMeasurementScenarioAsync()
    {
        await using var scope = Factory.Services.CreateAsyncScope();
        var database = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var trainer = await TestDataFactory.SeedUserAsync(
            database,
            "reporting-outbox-trainer",
            "reporting-outbox-trainer@example.com",
            "password123");
        var trainee = await TestDataFactory.SeedUserAsync(
            database,
            "reporting-outbox-trainee",
            "reporting-outbox-trainee@example.com",
            "password123");
        var template = new ReportTemplate
        {
            Id = Id<ReportTemplate>.New(),
            TrainerId = trainer.Id,
            Name = "Progress check-in"
        };
        var request = new ReportRequest
        {
            Id = Id<ReportRequest>.New(),
            TrainerId = trainer.Id,
            TraineeId = trainee.Id,
            TemplateId = template.Id,
            Status = ReportRequestStatus.Pending
        };

        template.Fields.Add(new ReportTemplateField
        {
            Id = Id<ReportTemplateField>.New(),
            TemplateId = template.Id,
            Key = "measurements",
            Label = "Measurements",
            Type = ReportFieldType.Measurements,
            IsRequired = true,
            Order = 0,
            ModuleConfig = """{"measurementTypes":["BodyWeight"]}"""
        });

        database.ReportTemplates.Add(template);
        database.ReportRequests.Add(request);
        await database.SaveChangesAsync();

        return new PendingMeasurementScenario(trainee, request.Id);
    }

    private async Task<PersistedSubmissionState> ReadSubmissionStateAsync(Id<ReportRequest> requestId)
    {
        await using var scope = Factory.Services.CreateAsyncScope();
        var database = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var request = await database.ReportRequests
            .AsNoTracking()
            .SingleAsync(candidate => candidate.Id == requestId);
        var submission = await database.ReportSubmissions
            .AsNoTracking()
            .SingleOrDefaultAsync(candidate => candidate.ReportRequestId == requestId);
        var acceptedProgressEnvelopes = await database.CommandEnvelopes
            .AsNoTracking()
            .Where(envelope => envelope.CommandTypeFullName == AcceptedProgressCommandId)
            .ToArrayAsync();

        return new PersistedSubmissionState(request.Status, submission, acceptedProgressEnvelopes);
    }

    private async Task<int> CountAcceptedProgressEnvelopesAsync()
    {
        await using var scope = Factory.Services.CreateAsyncScope();
        var database = scope.ServiceProvider.GetRequiredService<AppDbContext>();

        return await database.CommandEnvelopes.CountAsync(envelope => envelope.CommandTypeFullName == AcceptedProgressCommandId);
    }

    private async Task<CommandEnvelope> ReadEnvelopeAsync(Id<CommandEnvelope> envelopeId)
    {
        await using var scope = Factory.Services.CreateAsyncScope();
        var database = scope.ServiceProvider.GetRequiredService<AppDbContext>();

        return await database.CommandEnvelopes
            .AsNoTracking()
            .SingleAsync(envelope => envelope.Id == envelopeId);
    }

    private async Task<DeliveredAcceptedProgressState> ReadDeliveryStateAsync(
        Id<CommandEnvelope> envelopeId,
        Id<User> traineeId)
    {
        await using var scope = Factory.Services.CreateAsyncScope();
        var database = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var envelope = await database.CommandEnvelopes
            .AsNoTracking()
            .SingleAsync(candidate => candidate.Id == envelopeId);
        var measurements = await database.Measurements
            .AsNoTracking()
            .Where(candidate => candidate.UserId == traineeId && candidate.BodyPart == BodyParts.BodyWeight)
            .ToArrayAsync();

        return new DeliveredAcceptedProgressState(envelope, measurements);
    }

    private async Task ResetEnvelopeForReplayAsync(Id<CommandEnvelope> envelopeId)
    {
        await using var scope = Factory.Services.CreateAsyncScope();
        var database = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var envelope = await database.CommandEnvelopes.SingleAsync(candidate => candidate.Id == envelopeId);
        envelope.Status = ActionExecutionStatus.Pending;
        envelope.CompletedAt = null;
        envelope.ProcessingStartedAtUtc = null;
        envelope.NextAttemptAt = null;
        await database.SaveChangesAsync();
    }

    private async Task AuthenticateAsAsync(User user)
    {
        var response = await Client.PostAsJsonAsync("/api/login", new
        {
            name = user.Name,
            password = "password123"
        });
        response.StatusCode.Should().Be(HttpStatusCode.OK);

        using var loginJson = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        Client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue(
            "Bearer",
            ReadRequiredString(loginJson.RootElement, "token"));
    }

    private static SubmitReportRequestCommand CreateMeasurementSubmissionCommand()
    {
        using var document = JsonDocument.Parse("""
            {
              "measurements": {
                "BodyWeight": { "value": 82.4, "unit": "Kilograms" }
              }
            }
            """);

        return new SubmitReportRequestCommand
        {
            Answers = document.RootElement.EnumerateObject()
                .ToDictionary(property => property.Name, property => property.Value.Clone(), StringComparer.Ordinal)
        };
    }

    private static string ReadRequiredString(JsonElement element, string propertyName)
    {
        element.TryGetProperty(propertyName, out var property).Should().BeTrue($"response must contain {propertyName}");
        property.ValueKind.Should().Be(JsonValueKind.String, $"{propertyName} must remain a JSON string");
        var value = property.GetString();
        value.Should().NotBeNullOrWhiteSpace($"{propertyName} must not be empty");
        return value!;
    }

    private static async Task StageAcceptedProgressCommandAsync(
        IServiceProvider serviceProvider,
        ReportSubmission submission,
        Id<User> traineeId,
        Id<ReportRequest> requestId)
    {
        var applicationAssembly = typeof(IReportingService).Assembly;
        var commandType = applicationAssembly.GetType(AcceptedProgressCommandTypeName);
        var outboxWriterType = applicationAssembly.GetType(CommandOutboxWriterTypeName);

        commandType.Should().NotBeNull("#386 requires a Reporting-owned accepted-progress command contract");
        outboxWriterType.Should().NotBeNull("#386 requires a stage-only command outbox writer");

        var command = Activator.CreateInstance(commandType!)
            ?? throw new InvalidOperationException($"Could not create {AcceptedProgressCommandTypeName}.");
        var eventProperty = commandType!.GetProperties(BindingFlags.Instance | BindingFlags.Public)
            .SingleOrDefault(property => property.PropertyType == typeof(ReportSubmissionAcceptedProgressEvent));
        eventProperty.Should().NotBeNull("the accepted-progress command must carry the published event contract");
        eventProperty!.SetValue(command, CreateAcceptedProgressEvent(submission, traineeId, requestId));

        var outboxWriter = serviceProvider.GetService(outboxWriterType!)
            ?? throw new InvalidOperationException($"No {CommandOutboxWriterTypeName} is registered.");
        var stageMethod = outboxWriterType!.GetMethods(BindingFlags.Instance | BindingFlags.Public)
            .SingleOrDefault(method => method.Name == "StageAsync" && method.IsGenericMethodDefinition);
        stageMethod.Should().NotBeNull("the stage-only outbox writer must expose StageAsync<TCommand>");

        var invocation = stageMethod!.MakeGenericMethod(commandType).Invoke(
            outboxWriter,
            [command, CancellationToken.None]);
        if (invocation is not Task task)
        {
            throw new InvalidOperationException("ICommandOutboxWriter.StageAsync<TCommand> must return Task.");
        }

        await task;
    }

    private static ReportSubmissionAcceptedProgressEvent CreateAcceptedProgressEvent(
        ReportSubmission submission,
        Id<User> traineeId,
        Id<ReportRequest> requestId)
    {
        var acceptedAt = new DateTimeOffset(2026, 7, 21, 12, 0, 0, TimeSpan.Zero);

        return new ReportSubmissionAcceptedProgressEvent(
            ReportSubmissionAcceptedProgressEvent.CurrentSchemaVersion,
            Id<ReportSubmissionAcceptedProgressEvent>.New().ToString(),
            submission.Id.ToString(),
            requestId.ToString(),
            submission.Id.ToString(),
            traineeId,
            acceptedAt,
            acceptedAt,
            [new ReportSubmissionAcceptedMeasurement(BodyParts.BodyWeight, 82.4, MeasurementUnits.Kilograms)]);
    }

    private sealed record PendingMeasurementScenario(User Trainee, Id<ReportRequest> RequestId);

    private sealed record PersistedSubmissionState(
        ReportRequestStatus RequestStatus,
        ReportSubmission? Submission,
        IReadOnlyList<CommandEnvelope> AcceptedProgressEnvelopes);

    private sealed record DeliveredAcceptedProgressState(
        CommandEnvelope Envelope,
        IReadOnlyList<Measurement> Measurements);

    private sealed class ForcedFailureAfterOutboxStagingException : Exception;
}
