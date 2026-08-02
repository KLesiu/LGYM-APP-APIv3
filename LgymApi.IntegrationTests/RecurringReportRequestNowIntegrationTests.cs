using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using System.Text.Json.Serialization;
using FluentAssertions;
using LgymApi.Api.Features.Trainer.Contracts;
using LgymApi.Domain.Entities;
using LgymApi.Domain.Enums;
using LgymApi.Domain.Notifications;
using LgymApi.Domain.ValueObjects;
using LgymApi.Infrastructure.Data;
using LgymApi.Infrastructure.Data.SeedData;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace LgymApi.IntegrationTests;

[TestFixture]
public sealed class RecurringReportRequestNowIntegrationTests : IntegrationTestBase
{
    private const string CanonicalCommandId =
        "LgymApi.BackgroundWorker.Common.Commands.ReportRequestCreatedInAppNotificationCommand";

    [Test]
    public async Task RequestNow_EmptyBody_ProvesPendingVisibilityNotificationAndFeedbackReadScheduling()
    {
        var trainer = await SeedTrainerAsync("request-now-trainer", "request-now-trainer@example.com");
        var trainee = await SeedUserAsync("request-now-trainee", "request-now-trainee@example.com");
        await LinkTrainerAndTraineeAsync(trainer.Id, trainee.Id);
        SetAuthorizationHeader(trainer.Id);

        var templateResponse = await PostAsJsonWithApiOptionsAsync("/api/trainer/report-templates", new
        {
            name = "Request-now check-in",
            fields = new[]
            {
                new { key = "note", label = "Note", type = "Text", isRequired = true, order = 0 }
            }
        });
        templateResponse.StatusCode.Should().Be(HttpStatusCode.Created);
        var template = await ReadAsync<ReportTemplateDto>(templateResponse);

        var assignmentResponse = await PostAsJsonWithApiOptionsAsync(
            $"/api/trainer/trainees/{trainee.Id}/recurring-report-assignments",
            new
            {
                templateId = template.Id,
                intervalValue = 2,
                intervalUnit = RecurringReportIntervalUnit.Day,
                startsAt = DateTimeOffset.UtcNow.AddDays(-1),
                endsAt = DateTimeOffset.UtcNow.AddDays(30),
                note = "Manual cycle"
            });
        assignmentResponse.StatusCode.Should().Be(HttpStatusCode.Created);
        var assignment = await ReadAsync<RecurringReportAssignmentDto>(assignmentResponse);

        SetIdempotencyKey("request-now-happy-empty-body");
        var requestNowResponse = await Client.PostAsync(
            $"/api/trainer/trainees/{trainee.Id}/recurring-report-assignments/{assignment.Id}/request-now",
            content: null);
        ClearIdempotencyKey();

        requestNowResponse.StatusCode.Should().Be(HttpStatusCode.Created);
        var requestNowJson = await requestNowResponse.Content.ReadAsStringAsync();
        using (var document = JsonDocument.Parse(requestNowJson))
        {
            var root = document.RootElement;
            root.TryGetProperty("_id", out _).Should().BeTrue();
            root.TryGetProperty("id", out _).Should().BeFalse();
            root.TryGetProperty("envelopeId", out _).Should().BeFalse();
            root.TryGetProperty("currentReportRequestId", out _).Should().BeTrue();
            root.TryGetProperty("currentReportRequest", out var currentRequestJson).Should().BeTrue();
            currentRequestJson.GetProperty("status").GetString().Should().Be(nameof(ReportRequestStatus.Pending));
            currentRequestJson.TryGetProperty("_id", out _).Should().BeTrue();
            currentRequestJson.TryGetProperty("id", out _).Should().BeFalse();
        }

        var requestedAssignment = JsonSerializer.Deserialize<RecurringReportAssignmentDto>(requestNowJson, JsonOptions)!;
        requestedAssignment.Id.Should().Be(assignment.Id);
        requestedAssignment.CurrentReportRequest.Should().NotBeNull();
        requestedAssignment.CurrentReportRequestId.Should().Be(requestedAssignment.CurrentReportRequest!.Id);
        requestedAssignment.CurrentReportRequest.Status.Should().Be(ReportRequestStatus.Pending);
        requestedAssignment.NextEligibleAt.Should().BeNull();
        var requestId = ParseId<ReportRequest>(requestedAssignment.CurrentReportRequest.Id);
        var assignmentId = ParseId<RecurringReportAssignment>(assignment.Id);

        await AssertStagedCanonicalCommandAndNoScheduleAsync(trainer.Id, trainee.Id, requestId, assignmentId);

        SetAuthorizationHeader(trainee.Id);
        var pendingResponse = await Client.GetAsync("/api/trainee/report-requests");
        pendingResponse.StatusCode.Should().Be(HttpStatusCode.OK);
        var pending = await ReadAsync<List<ReportRequestDto>>(pendingResponse);
        pending.Should().ContainSingle(item =>
            item.Id == requestId.ToString() && item.Status == ReportRequestStatus.Pending);

        await ProcessPendingCommandsAsync();
        await AssertCanonicalInAppDeliveryAsync(trainer.Id, trainee.Id, requestId);

        var submitResponse = await PostAsJsonWithApiOptionsAsync(
            $"/api/trainee/report-requests/{requestId}/submit",
            new { answers = new { note = "Ready for feedback" } });
        submitResponse.StatusCode.Should().Be(HttpStatusCode.OK);
        var submission = await ReadAsync<ReportSubmissionDto>(submitResponse);
        await AssertNextEligibleAtIsNullAsync(assignmentId);

        SetAuthorizationHeader(trainer.Id);
        var feedbackResponse = await PostAsJsonWithApiOptionsAsync(
            $"/api/trainer/trainees/{trainee.Id}/report-submissions/{submission.Id}/feedback",
            new
            {
                trainerOverallComment = "Good progress",
                trainerFieldComments = new Dictionary<string, string?> { ["note"] = "Keep going" }
            });
        feedbackResponse.StatusCode.Should().Be(HttpStatusCode.OK);
        await AssertNextEligibleAtIsNullAsync(assignmentId);

        SetAuthorizationHeader(trainee.Id);
        var feedbackReadResponse = await Client.PostAsync(
            $"/api/trainee/report-submissions/{submission.Id}/mark-feedback-read",
            content: null);
        feedbackReadResponse.StatusCode.Should().Be(HttpStatusCode.OK);
        var feedbackRead = await ReadAsync<ReportSubmissionDto>(feedbackReadResponse);
        feedbackRead.TrainerFeedbackReadAt.Should().NotBeNull();

        using var verificationScope = Factory.Services.CreateScope();
        var verificationDatabase = verificationScope.ServiceProvider.GetRequiredService<AppDbContext>();
        var storedSubmission = await verificationDatabase.ReportSubmissions
            .AsNoTracking()
            .SingleAsync(candidate => candidate.Id == ParseId<ReportSubmission>(submission.Id));
        var storedAssignment = await verificationDatabase.RecurringReportAssignments
            .AsNoTracking()
            .SingleAsync(candidate => candidate.Id == assignmentId);
        storedSubmission.TrainerFeedbackAddedAt.Should().NotBeNull();
        storedSubmission.TrainerFeedbackReadAt.Should().Be(feedbackRead.TrainerFeedbackReadAt);
        storedAssignment.NextEligibleAt.Should().Be(storedSubmission.TrainerFeedbackReadAt!.Value.AddDays(2));
        storedAssignment.NextEligibleAt.Should().BeAfter(storedAssignment.LastRequestCreatedAt!.Value);
    }

    private async Task AssertStagedCanonicalCommandAndNoScheduleAsync(
        Id<User> trainerId,
        Id<User> traineeId,
        Id<ReportRequest> requestId,
        Id<RecurringReportAssignment> assignmentId)
    {
        using var scope = Factory.Services.CreateScope();
        var database = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var envelope = await database.CommandEnvelopes.AsNoTracking().SingleAsync(candidate =>
            candidate.CommandTypeFullName == CanonicalCommandId);
        using var payload = JsonDocument.Parse(envelope.PayloadJson);
        payload.RootElement.GetProperty("requestId").GetString().Should().Be(requestId.ToString());
        payload.RootElement.GetProperty("traineeId").GetString().Should().Be(traineeId.ToString());
        payload.RootElement.GetProperty("trainerId").GetString().Should().Be(trainerId.ToString());
        payload.RootElement.TryGetProperty("envelopeId", out _).Should().BeFalse();
        var storedAssignment = await database.RecurringReportAssignments.AsNoTracking()
            .SingleAsync(candidate => candidate.Id == assignmentId);
        storedAssignment.NextEligibleAt.Should().BeNull();
    }

    private async Task AssertCanonicalInAppDeliveryAsync(
        Id<User> trainerId,
        Id<User> traineeId,
        Id<ReportRequest> requestId)
    {
        using var scope = Factory.Services.CreateScope();
        var database = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var envelope = await database.CommandEnvelopes.AsNoTracking().SingleAsync(candidate =>
            candidate.CommandTypeFullName == CanonicalCommandId);
        envelope.Status.Should().Be(ActionExecutionStatus.Completed);
        var notification = await database.InAppNotifications.AsNoTracking().SingleAsync(candidate =>
            candidate.RecipientId == traineeId && candidate.Type == InAppNotificationTypes.ReportRequestReceived);
        notification.SenderUserId.Should().Be(trainerId);
        notification.DeliveryKey.Should().Be($"report-request:{requestId}:created");
        notification.RedirectUrl.Should().Be($"/trainer/report-requests/{requestId}");
        notification.IsRead.Should().BeFalse();
    }

    private async Task AssertNextEligibleAtIsNullAsync(Id<RecurringReportAssignment> assignmentId)
    {
        using var scope = Factory.Services.CreateScope();
        var database = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var nextEligibleAt = await database.RecurringReportAssignments.AsNoTracking()
            .Where(candidate => candidate.Id == assignmentId)
            .Select(candidate => candidate.NextEligibleAt)
            .SingleAsync();
        nextEligibleAt.Should().BeNull();
    }

    private async Task<User> SeedTrainerAsync(string name, string email)
    {
        var trainer = await SeedUserAsync(name, email);
        using var scope = Factory.Services.CreateScope();
        var database = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        database.UserRoles.Add(new UserRole
        {
            UserId = trainer.Id,
            RoleId = RoleSeedDataConfiguration.TrainerRoleSeedId
        });
        await database.SaveChangesAsync();
        return trainer;
    }

    private async Task LinkTrainerAndTraineeAsync(Id<User> trainerId, Id<User> traineeId)
    {
        using var scope = Factory.Services.CreateScope();
        var database = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        database.TrainerTraineeLinks.Add(new TrainerTraineeLink
        {
            Id = Id<TrainerTraineeLink>.New(),
            TrainerId = trainerId,
            TraineeId = traineeId
        });
        await database.SaveChangesAsync();
    }

    private static async Task<T> ReadAsync<T>(HttpResponseMessage response) where T : notnull =>
        (await response.Content.ReadFromJsonAsync<T>(JsonOptions))!;

    private static Id<TEntity> ParseId<TEntity>(string value)
    {
        Id<TEntity>.TryParse(value, out var id).Should().BeTrue();
        return id;
    }

    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web)
    {
        Converters = { new JsonStringEnumConverter() }
    };
}
