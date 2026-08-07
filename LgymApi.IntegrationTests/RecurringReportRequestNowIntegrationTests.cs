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
using LgymApi.IntegrationTests.Authorization;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace LgymApi.IntegrationTests;

[TestFixture]
[Category("PostgreSql")]
public sealed class RecurringReportRequestNowIntegrationTests : PostgreSqlRecurringReportHttpTestBase
{
    private const string CanonicalCommandId =
        "LgymApi.BackgroundWorker.Common.Commands.ReportRequestCreatedInAppNotificationCommand";

    [Test]
    [AuthorizationEvidence("POST", "/api/trainer/trainees/{traineeId}/recurring-report-assignments", "trainer-shared", "active-relationship-allow")]
    [AuthorizationEvidence("POST", "/api/trainer/trainees/{traineeId}/recurring-report-assignments", "trainer-shared", "unrelated-relationship-denial")]
    [AuthorizationEvidence("POST", "/api/trainer/trainees/{traineeId}/recurring-report-assignments", "trainer-shared", "former-relationship-denial")]
    [AuthorizationEvidence("POST", "/api/trainer/trainees/{traineeId}/recurring-report-assignments", "trainer-shared", "foreign-object-denial-no-mutation")]
    [AuthorizationEvidence("POST", "/api/trainer/trainees/{traineeId}/recurring-report-assignments", "trainer-shared", "anonymous-denial")]
    [AuthorizationEvidence("POST", "/api/trainer/trainees/{traineeId}/recurring-report-assignments/{id}/delete", "trainer-shared", "active-relationship-allow")]
    [AuthorizationEvidence("POST", "/api/trainer/trainees/{traineeId}/recurring-report-assignments/{id}/delete", "trainer-shared", "unrelated-relationship-denial")]
    [AuthorizationEvidence("POST", "/api/trainer/trainees/{traineeId}/recurring-report-assignments/{id}/delete", "trainer-shared", "former-relationship-denial")]
    [AuthorizationEvidence("POST", "/api/trainer/trainees/{traineeId}/recurring-report-assignments/{id}/delete", "trainer-shared", "foreign-object-denial-no-mutation")]
    [AuthorizationEvidence("POST", "/api/trainer/trainees/{traineeId}/recurring-report-assignments/{id}/delete", "trainer-shared", "anonymous-denial")]
    [AuthorizationEvidence("POST", "/api/trainer/trainees/{traineeId}/recurring-report-assignments/{id}/pause", "trainer-shared", "active-relationship-allow")]
    [AuthorizationEvidence("POST", "/api/trainer/trainees/{traineeId}/recurring-report-assignments/{id}/pause", "trainer-shared", "unrelated-relationship-denial")]
    [AuthorizationEvidence("POST", "/api/trainer/trainees/{traineeId}/recurring-report-assignments/{id}/pause", "trainer-shared", "former-relationship-denial")]
    [AuthorizationEvidence("POST", "/api/trainer/trainees/{traineeId}/recurring-report-assignments/{id}/pause", "trainer-shared", "foreign-object-denial-no-mutation")]
    [AuthorizationEvidence("POST", "/api/trainer/trainees/{traineeId}/recurring-report-assignments/{id}/pause", "trainer-shared", "anonymous-denial")]
    [AuthorizationEvidence("POST", "/api/trainer/trainees/{traineeId}/recurring-report-assignments/{id}/request-now", "trainer-shared", "active-relationship-allow")]
    [AuthorizationEvidence("POST", "/api/trainer/trainees/{traineeId}/recurring-report-assignments/{id}/request-now", "trainer-shared", "unrelated-relationship-denial")]
    [AuthorizationEvidence("POST", "/api/trainer/trainees/{traineeId}/recurring-report-assignments/{id}/request-now", "trainer-shared", "former-relationship-denial")]
    [AuthorizationEvidence("POST", "/api/trainer/trainees/{traineeId}/recurring-report-assignments/{id}/request-now", "trainer-shared", "foreign-object-denial-no-mutation")]
    [AuthorizationEvidence("POST", "/api/trainer/trainees/{traineeId}/recurring-report-assignments/{id}/request-now", "trainer-shared", "anonymous-denial")]
    [AuthorizationEvidence("POST", "/api/trainer/trainees/{traineeId}/recurring-report-assignments/{id}/resume", "trainer-shared", "active-relationship-allow")]
    [AuthorizationEvidence("POST", "/api/trainer/trainees/{traineeId}/recurring-report-assignments/{id}/resume", "trainer-shared", "unrelated-relationship-denial")]
    [AuthorizationEvidence("POST", "/api/trainer/trainees/{traineeId}/recurring-report-assignments/{id}/resume", "trainer-shared", "former-relationship-denial")]
    [AuthorizationEvidence("POST", "/api/trainer/trainees/{traineeId}/recurring-report-assignments/{id}/resume", "trainer-shared", "foreign-object-denial-no-mutation")]
    [AuthorizationEvidence("POST", "/api/trainer/trainees/{traineeId}/recurring-report-assignments/{id}/resume", "trainer-shared", "anonymous-denial")]
    [AuthorizationEvidence("POST", "/api/trainer/trainees/{traineeId}/recurring-report-assignments/{id}/update", "trainer-shared", "active-relationship-allow")]
    [AuthorizationEvidence("POST", "/api/trainer/trainees/{traineeId}/recurring-report-assignments/{id}/update", "trainer-shared", "unrelated-relationship-denial")]
    [AuthorizationEvidence("POST", "/api/trainer/trainees/{traineeId}/recurring-report-assignments/{id}/update", "trainer-shared", "former-relationship-denial")]
    [AuthorizationEvidence("POST", "/api/trainer/trainees/{traineeId}/recurring-report-assignments/{id}/update", "trainer-shared", "foreign-object-denial-no-mutation")]
    [AuthorizationEvidence("POST", "/api/trainer/trainees/{traineeId}/recurring-report-assignments/{id}/update", "trainer-shared", "anonymous-denial")]
    [AuthorizationEvidence("POST", "/api/trainee/report-submissions/{submissionId}/mark-feedback-read", "own", "owner-allow")]
    [AuthorizationEvidence("POST", "/api/trainee/report-submissions/{submissionId}/mark-feedback-read", "own", "foreign-object-denial-no-mutation")]
    [AuthorizationEvidence("POST", "/api/trainee/report-submissions/{submissionId}/mark-feedback-read", "own", "anonymous-denial")]
    [AuthorizationEvidence("POST", "/api/trainer/trainees/{traineeId}/report-submissions/{submissionId}/feedback", "trainer-shared", "active-relationship-allow")]
    [AuthorizationEvidence("POST", "/api/trainer/trainees/{traineeId}/report-submissions/{submissionId}/feedback", "trainer-shared", "unrelated-relationship-denial")]
    [AuthorizationEvidence("POST", "/api/trainer/trainees/{traineeId}/report-submissions/{submissionId}/feedback", "trainer-shared", "former-relationship-denial")]
    [AuthorizationEvidence("POST", "/api/trainer/trainees/{traineeId}/report-submissions/{submissionId}/feedback", "trainer-shared", "foreign-object-denial-no-mutation")]
    [AuthorizationEvidence("POST", "/api/trainer/trainees/{traineeId}/report-submissions/{submissionId}/feedback", "trainer-shared", "anonymous-denial")]
    public async Task RequestNow_EmptyBody_ProvesPendingVisibilityNotificationAndFeedbackReadScheduling()
    {
        var trainer = await SeedTrainerAsync("request-now-trainer", "request-now-trainer@example.com");
        var otherTrainer = await SeedTrainerAsync("request-now-other-trainer", "request-now-other-trainer@example.com");
        var trainee = await SeedUserAsync("request-now-trainee", "request-now-trainee@example.com");
        var otherTrainee = await SeedUserAsync("request-now-other-trainee", "request-now-other-trainee@example.com");
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

        var feedbackRequest = new
        {
            trainerOverallComment = "Blocked feedback",
            trainerFieldComments = new Dictionary<string, string?> { ["note"] = "Blocked" }
        };
        SetAuthorizationHeader(otherTrainer.Id);
        var foreignFeedbackResponse = await PostAsJsonWithApiOptionsAsync($"/api/trainer/trainees/{trainee.Id}/report-submissions/{submission.Id}/feedback", feedbackRequest);
        foreignFeedbackResponse.StatusCode.Should().Be(HttpStatusCode.NotFound);
        SetAuthorizationHeader(otherTrainee.Id);
        var ordinaryFeedbackResponse = await PostAsJsonWithApiOptionsAsync($"/api/trainer/trainees/{trainee.Id}/report-submissions/{submission.Id}/feedback", feedbackRequest);
        ordinaryFeedbackResponse.StatusCode.Should().Be(HttpStatusCode.Forbidden);
        Client.DefaultRequestHeaders.Authorization = null;
        var anonymousFeedbackResponse = await PostAsJsonWithApiOptionsAsync($"/api/trainer/trainees/{trainee.Id}/report-submissions/{submission.Id}/feedback", feedbackRequest);
        anonymousFeedbackResponse.StatusCode.Should().Be(HttpStatusCode.Unauthorized);

        SetAuthorizationHeader(otherTrainee.Id);
        using var foreignFeedbackReadResponse = await Client.PostAsync(
            $"/api/trainee/report-submissions/{submission.Id}/mark-feedback-read",
            content: null);
        foreignFeedbackReadResponse.StatusCode.Should().Be(HttpStatusCode.NotFound);

        Client.DefaultRequestHeaders.Authorization = null;
        using var anonymousFeedbackReadResponse = await Client.PostAsync(
            $"/api/trainee/report-submissions/{submission.Id}/mark-feedback-read",
            content: null);
        anonymousFeedbackReadResponse.StatusCode.Should().Be(HttpStatusCode.Unauthorized);

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
        storedSubmission.TrainerFeedbackReadAt.Should().NotBeNull();
        storedSubmission.TrainerFeedbackReadAt!.Value.Should().BeCloseTo(
            feedbackRead.TrainerFeedbackReadAt!.Value,
            TimeSpan.FromMicroseconds(1));
        storedAssignment.NextEligibleAt.Should().Be(storedSubmission.TrainerFeedbackReadAt!.Value.AddDays(2));
        storedAssignment.NextEligibleAt.Should().BeAfter(storedAssignment.LastRequestCreatedAt!.Value);

        SetAuthorizationHeader(trainer.Id);
        var unrelatedCreateResponse = await PostAsJsonWithApiOptionsAsync(
            $"/api/trainer/trainees/{otherTrainee.Id}/recurring-report-assignments",
            new { templateId = template.Id, intervalValue = 2, intervalUnit = RecurringReportIntervalUnit.Day, startsAt = DateTimeOffset.UtcNow, note = "Blocked unrelated" });
        unrelatedCreateResponse.StatusCode.Should().Be(HttpStatusCode.NotFound);

        SetAuthorizationHeader(otherTrainee.Id);
        var ordinaryCreateResponse = await PostAsJsonWithApiOptionsAsync(
            $"/api/trainer/trainees/{trainee.Id}/recurring-report-assignments",
            new { templateId = template.Id, intervalValue = 2, intervalUnit = RecurringReportIntervalUnit.Day, startsAt = DateTimeOffset.UtcNow, note = "Blocked ordinary" });
        ordinaryCreateResponse.StatusCode.Should().Be(HttpStatusCode.Forbidden);

        Client.DefaultRequestHeaders.Authorization = null;
        var anonymousCreateResponse = await PostAsJsonWithApiOptionsAsync(
            $"/api/trainer/trainees/{trainee.Id}/recurring-report-assignments",
            new { templateId = template.Id, intervalValue = 2, intervalUnit = RecurringReportIntervalUnit.Day, startsAt = DateTimeOffset.UtcNow, note = "Blocked anonymous" });
        anonymousCreateResponse.StatusCode.Should().Be(HttpStatusCode.Unauthorized);

        SetAuthorizationHeader(trainer.Id);
        var ownerPauseResponse = await Client.PostAsync(
            $"/api/trainer/trainees/{trainee.Id}/recurring-report-assignments/{assignment.Id}/pause",
            content: null);
        ownerPauseResponse.StatusCode.Should().Be(HttpStatusCode.OK);

        var ownerResumeResponse = await Client.PostAsync(
            $"/api/trainer/trainees/{trainee.Id}/recurring-report-assignments/{assignment.Id}/resume",
            content: null);
        ownerResumeResponse.StatusCode.Should().Be(HttpStatusCode.OK);

        var foreignPauseResponse = await Client.PostAsync(
            $"/api/trainer/trainees/{otherTrainee.Id}/recurring-report-assignments/{assignment.Id}/pause",
            content: null);
        foreignPauseResponse.StatusCode.Should().Be(HttpStatusCode.NotFound);
        var foreignResumeResponse = await Client.PostAsync(
            $"/api/trainer/trainees/{otherTrainee.Id}/recurring-report-assignments/{assignment.Id}/resume",
            content: null);
        foreignResumeResponse.StatusCode.Should().Be(HttpStatusCode.NotFound);

        SetAuthorizationHeader(otherTrainee.Id);
        var ordinaryPauseResponse = await Client.PostAsync(
            $"/api/trainer/trainees/{trainee.Id}/recurring-report-assignments/{assignment.Id}/pause",
            content: null);
        ordinaryPauseResponse.StatusCode.Should().Be(HttpStatusCode.Forbidden);
        var ordinaryResumeResponse = await Client.PostAsync(
            $"/api/trainer/trainees/{trainee.Id}/recurring-report-assignments/{assignment.Id}/resume",
            content: null);
        ordinaryResumeResponse.StatusCode.Should().Be(HttpStatusCode.Forbidden);

        Client.DefaultRequestHeaders.Authorization = null;
        var anonymousPauseResponse = await Client.PostAsync(
            $"/api/trainer/trainees/{trainee.Id}/recurring-report-assignments/{assignment.Id}/pause",
            content: null);
        anonymousPauseResponse.StatusCode.Should().Be(HttpStatusCode.Unauthorized);
        var anonymousResumeResponse = await Client.PostAsync(
            $"/api/trainer/trainees/{trainee.Id}/recurring-report-assignments/{assignment.Id}/resume",
            content: null);
        anonymousResumeResponse.StatusCode.Should().Be(HttpStatusCode.Unauthorized);

        SetAuthorizationHeader(trainer.Id);
        var foreignRequestNowResponse = await Client.PostAsync(
            $"/api/trainer/trainees/{otherTrainee.Id}/recurring-report-assignments/{assignment.Id}/request-now",
            content: null);
        foreignRequestNowResponse.StatusCode.Should().Be(HttpStatusCode.NotFound);

        SetAuthorizationHeader(otherTrainee.Id);
        var ordinaryRequestNowResponse = await Client.PostAsync(
            $"/api/trainer/trainees/{trainee.Id}/recurring-report-assignments/{assignment.Id}/request-now",
            content: null);
        ordinaryRequestNowResponse.StatusCode.Should().Be(HttpStatusCode.Forbidden);

        Client.DefaultRequestHeaders.Authorization = null;
        var anonymousRequestNowResponse = await Client.PostAsync(
            $"/api/trainer/trainees/{trainee.Id}/recurring-report-assignments/{assignment.Id}/request-now",
            content: null);
        anonymousRequestNowResponse.StatusCode.Should().Be(HttpStatusCode.Unauthorized);

        SetAuthorizationHeader(trainer.Id);
        var ownerUpdateResponse = await PostAsJsonWithApiOptionsAsync(
            $"/api/trainer/trainees/{trainee.Id}/recurring-report-assignments/{assignment.Id}/update",
            new { templateId = template.Id, intervalValue = 3, intervalUnit = RecurringReportIntervalUnit.Day, startsAt = DateTimeOffset.UtcNow, note = "Updated cycle" });
        ownerUpdateResponse.StatusCode.Should().Be(HttpStatusCode.OK);

        var foreignUpdateResponse = await PostAsJsonWithApiOptionsAsync(
            $"/api/trainer/trainees/{otherTrainee.Id}/recurring-report-assignments/{assignment.Id}/update",
            new { templateId = template.Id, intervalValue = 3, intervalUnit = RecurringReportIntervalUnit.Day, startsAt = DateTimeOffset.UtcNow, note = "Blocked unrelated update" });
        foreignUpdateResponse.StatusCode.Should().Be(HttpStatusCode.NotFound);

        SetAuthorizationHeader(otherTrainee.Id);
        var ordinaryUpdateResponse = await PostAsJsonWithApiOptionsAsync(
            $"/api/trainer/trainees/{trainee.Id}/recurring-report-assignments/{assignment.Id}/update",
            new { templateId = template.Id, intervalValue = 3, intervalUnit = RecurringReportIntervalUnit.Day, startsAt = DateTimeOffset.UtcNow, note = "Blocked ordinary update" });
        ordinaryUpdateResponse.StatusCode.Should().Be(HttpStatusCode.Forbidden);

        Client.DefaultRequestHeaders.Authorization = null;
        var anonymousUpdateResponse = await PostAsJsonWithApiOptionsAsync(
            $"/api/trainer/trainees/{trainee.Id}/recurring-report-assignments/{assignment.Id}/update",
            new { templateId = template.Id, intervalValue = 3, intervalUnit = RecurringReportIntervalUnit.Day, startsAt = DateTimeOffset.UtcNow, note = "Blocked anonymous update" });
        anonymousUpdateResponse.StatusCode.Should().Be(HttpStatusCode.Unauthorized);

        SetAuthorizationHeader(trainer.Id);
        var ownerDeleteResponse = await Client.PostAsync(
            $"/api/trainer/trainees/{trainee.Id}/recurring-report-assignments/{assignment.Id}/delete",
            content: null);
        ownerDeleteResponse.StatusCode.Should().Be(HttpStatusCode.OK);

        var foreignDeleteResponse = await Client.PostAsync(
            $"/api/trainer/trainees/{otherTrainee.Id}/recurring-report-assignments/{assignment.Id}/delete",
            content: null);
        foreignDeleteResponse.StatusCode.Should().Be(HttpStatusCode.NotFound);

        SetAuthorizationHeader(otherTrainee.Id);
        var ordinaryDeleteResponse = await Client.PostAsync(
            $"/api/trainer/trainees/{trainee.Id}/recurring-report-assignments/{assignment.Id}/delete",
            content: null);
        ordinaryDeleteResponse.StatusCode.Should().Be(HttpStatusCode.Forbidden);

        Client.DefaultRequestHeaders.Authorization = null;
        var anonymousDeleteResponse = await Client.PostAsync(
            $"/api/trainer/trainees/{trainee.Id}/recurring-report-assignments/{assignment.Id}/delete",
            content: null);
        anonymousDeleteResponse.StatusCode.Should().Be(HttpStatusCode.Unauthorized);

        SetAuthorizationHeader(trainer.Id);
        using (var unlinkScope = Factory.Services.CreateScope())
        {
            var database = unlinkScope.ServiceProvider.GetRequiredService<AppDbContext>();
            var link = await database.TrainerTraineeLinks.SingleAsync(candidate => candidate.TrainerId == trainer.Id && candidate.TraineeId == trainee.Id);
            database.TrainerTraineeLinks.Remove(link);
            await database.SaveChangesAsync();
        }

        var formerCreateResponse = await PostAsJsonWithApiOptionsAsync(
            $"/api/trainer/trainees/{trainee.Id}/recurring-report-assignments",
            new { templateId = template.Id, intervalValue = 2, intervalUnit = RecurringReportIntervalUnit.Day, startsAt = DateTimeOffset.UtcNow, note = "Blocked former" });
        formerCreateResponse.StatusCode.Should().Be(HttpStatusCode.NotFound);
        var formerFeedbackResponse = await PostAsJsonWithApiOptionsAsync($"/api/trainer/trainees/{trainee.Id}/report-submissions/{submission.Id}/feedback", feedbackRequest);
        formerFeedbackResponse.StatusCode.Should().Be(HttpStatusCode.NotFound);
        var formerPauseResponse = await Client.PostAsync(
            $"/api/trainer/trainees/{trainee.Id}/recurring-report-assignments/{assignment.Id}/pause",
            content: null);
        formerPauseResponse.StatusCode.Should().Be(HttpStatusCode.NotFound);
        var formerResumeResponse = await Client.PostAsync(
            $"/api/trainer/trainees/{trainee.Id}/recurring-report-assignments/{assignment.Id}/resume",
            content: null);
        formerResumeResponse.StatusCode.Should().Be(HttpStatusCode.NotFound);
        var formerUpdateResponse = await PostAsJsonWithApiOptionsAsync(
            $"/api/trainer/trainees/{trainee.Id}/recurring-report-assignments/{assignment.Id}/update",
            new { templateId = template.Id, intervalValue = 3, intervalUnit = RecurringReportIntervalUnit.Day, startsAt = DateTimeOffset.UtcNow, note = "Blocked former update" });
        formerUpdateResponse.StatusCode.Should().Be(HttpStatusCode.NotFound);
        var formerRequestNowResponse = await Client.PostAsync(
            $"/api/trainer/trainees/{trainee.Id}/recurring-report-assignments/{assignment.Id}/request-now",
            content: null);
        formerRequestNowResponse.StatusCode.Should().Be(HttpStatusCode.NotFound);
        var formerDeleteResponse = await Client.PostAsync(
            $"/api/trainer/trainees/{trainee.Id}/recurring-report-assignments/{assignment.Id}/delete",
            content: null);
        formerDeleteResponse.StatusCode.Should().Be(HttpStatusCode.NotFound);
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
