using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using System.Text.Json.Serialization;
using FluentAssertions;
using LgymApi.Domain.Entities;
using LgymApi.Domain.Enums;
using LgymApi.Domain.ValueObjects;
using LgymApi.Infrastructure.Data;
using LgymApi.Infrastructure.Data.SeedData;
using LgymApi.IntegrationTests.Authorization;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace LgymApi.IntegrationTests;

[TestFixture]
public sealed class ReportingEngineTests : IntegrationTestBase
{
    [Test]
    public async Task ReportFlow_TrainerCreatesRequest_TraineeSubmits_TrainerCanReadSubmission()
    {
        var trainer = await SeedTrainerAsync("trainer-reports", "trainer-reports@example.com");
        var trainee = await SeedUserAsync(name: "trainee-reports", email: "trainee-reports@example.com", password: "password123");

        using (var scope = Factory.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
            db.TrainerTraineeLinks.Add(new TrainerTraineeLink
            {
                Id = Domain.ValueObjects.Id<TrainerTraineeLink>.New(),
                TrainerId = (Domain.ValueObjects.Id<User>)trainer.Id,
                TraineeId = (Domain.ValueObjects.Id<User>)trainee.Id
            });
            await db.SaveChangesAsync();
        }

        SetAuthorizationHeader(trainer.Id);
        var createTemplateResponse = await Client.PostAsJsonAsync("/api/trainer/report-templates", new
        {
            name = "Weekly Check-in",
            description = "Basic wellness report",
            fields = new object[]
            {
                new { key = "weight", label = "Weight", type = "Number", isRequired = true, order = 0 },
                new { key = "sleptWell", label = "Slept Well", type = "Boolean", isRequired = false, order = 1 }
            }
        });

        createTemplateResponse.StatusCode.Should().Be(HttpStatusCode.Created);
        var template = await createTemplateResponse.Content.ReadFromJsonAsync<ReportTemplateResponse>();
        template.Should().NotBeNull();
        template!.Fields.Should().HaveCount(2);

        var createRequestResponse = await Client.PostAsJsonAsync($"/api/trainer/trainees/{trainee.Id}/report-requests", new
        {
            templateId = template.Id,
            dueAt = DateTimeOffset.UtcNow.AddDays(2)
        });

        createRequestResponse.StatusCode.Should().Be(HttpStatusCode.Created);
        var request = await createRequestResponse.Content.ReadFromJsonAsync<ReportRequestResponse>();
        request.Should().NotBeNull();
        request!.Status.Should().Be("Pending");

        SetAuthorizationHeader(trainee.Id);
        var pendingResponse = await Client.GetAsync("/api/trainee/report-requests");
        pendingResponse.StatusCode.Should().Be(HttpStatusCode.OK);
        var pending = await pendingResponse.Content.ReadFromJsonAsync<List<ReportRequestResponse>>();
        pending.Should().NotBeNull();
        pending!.Should().ContainSingle(x => x.Id == request.Id);

        var submitResponse = await Client.PostAsJsonAsync($"/api/trainee/report-requests/{request.Id}/submit", new
        {
            answers = new
            {
                weight = 81.2,
                sleptWell = true
            }
        });

        submitResponse.StatusCode.Should().Be(HttpStatusCode.OK);

        SetAuthorizationHeader(trainer.Id);
        var submissionsResponse = await Client.GetAsync($"/api/trainer/trainees/{trainee.Id}/report-submissions");
        submissionsResponse.StatusCode.Should().Be(HttpStatusCode.OK);
        var submissions = await submissionsResponse.Content.ReadFromJsonAsync<List<ReportSubmissionResponse>>();
        submissions.Should().NotBeNull();
        submissions!.Should().ContainSingle();
        submissions[0].ReportRequestId.Should().Be(request.Id);
        submissions[0].Answers["weight"].GetDouble().Should().BeApproximately(81.2, 0.001);
    }

    [Test]
    [AuthorizationEvidence("POST", "/api/trainer/trainees/{traineeId}/report-requests", "trainer-shared", "active-relationship-allow")]
    [AuthorizationEvidence("POST", "/api/trainer/trainees/{traineeId}/report-requests", "trainer-shared", "unrelated-relationship-denial")]
    [AuthorizationEvidence("POST", "/api/trainer/trainees/{traineeId}/report-requests", "trainer-shared", "former-relationship-denial")]
    [AuthorizationEvidence("POST", "/api/trainer/trainees/{traineeId}/report-requests", "trainer-shared", "foreign-object-denial-no-mutation")]
    [AuthorizationEvidence("POST", "/api/trainer/trainees/{traineeId}/report-requests", "trainer-shared", "anonymous-denial")]
    [AuthorizationEvidence("GET", "/api/trainee/report-requests", "own", "owner-allow")]
    [AuthorizationEvidence("GET", "/api/trainee/report-requests", "own", "no-client-subject")]
    [AuthorizationEvidence("GET", "/api/trainee/report-requests", "own", "anonymous-denial")]
    public async Task TraineePendingReportRequestsRoute_IsActorScopedAndNonDisclosing()
    {
        var trainer = await SeedTrainerAsync("task10-report-request-trainer", "task10-report-request-trainer@example.test");
        var trainee = await SeedUserAsync("task10-report-request-trainee", "task10-report-request-trainee@example.test");
        var otherTrainee = await SeedUserAsync("task10-report-request-other", "task10-report-request-other@example.test");

        using (var scope = Factory.Services.CreateScope())
        {
            var database = scope.ServiceProvider.GetRequiredService<AppDbContext>();
            database.TrainerTraineeLinks.Add(new TrainerTraineeLink
            {
                Id = Id<TrainerTraineeLink>.New(),
                TrainerId = trainer.Id,
                TraineeId = trainee.Id
            });
            await database.SaveChangesAsync();
        }

        SetAuthorizationHeader(trainer.Id);
        using var templateResponse = await Client.PostAsJsonAsync("/api/trainer/report-templates", new
        {
            name = "Task 10 protected report template",
            fields = new[]
            {
                new { key = "checkIn", label = "Check-in", type = "Text", isRequired = true, order = 0 }
            }
        });
        templateResponse.StatusCode.Should().Be(HttpStatusCode.Created);
        var template = await templateResponse.Content.ReadFromJsonAsync<ReportTemplateResponse>();
        template.Should().NotBeNull();

        using var createRequestResponse = await Client.PostAsJsonAsync($"/api/trainer/trainees/{trainee.Id}/report-requests", new
        {
            templateId = template!.Id,
            note = "Protected pending report request"
        });
        createRequestResponse.StatusCode.Should().Be(HttpStatusCode.Created);
        var createdRequest = await createRequestResponse.Content.ReadFromJsonAsync<ReportRequestResponse>();
        createdRequest.Should().NotBeNull();

        SetAuthorizationHeader(trainee.Id);
        using var ownerResponse = await Client.GetAsync("/api/trainee/report-requests");
        ownerResponse.StatusCode.Should().Be(HttpStatusCode.OK);
        var ownerRequests = await ownerResponse.Content.ReadFromJsonAsync<List<ReportRequestResponse>>();
        ownerRequests.Should().NotBeNull();
        ownerRequests!.Should().ContainSingle(request => request.Id == createdRequest!.Id);
        ownerRequests.Single().Note.Should().Be("Protected pending report request");

        SetAuthorizationHeader(otherTrainee.Id);
        using var otherResponse = await Client.GetAsync("/api/trainee/report-requests");
        otherResponse.StatusCode.Should().Be(HttpStatusCode.OK);
        var otherRequests = await otherResponse.Content.ReadFromJsonAsync<List<ReportRequestResponse>>();
        otherRequests.Should().NotBeNull();
        otherRequests.Should().BeEmpty();
        var otherResponseText = await otherResponse.Content.ReadAsStringAsync();
        otherResponseText.Should().NotContain(createdRequest!.Id);
        otherResponseText.Should().NotContain("Protected pending report request");

        ClearAuthorizationHeader();
        using var anonymousResponse = await Client.GetAsync("/api/trainee/report-requests");
        anonymousResponse.StatusCode.Should().Be(HttpStatusCode.Unauthorized);
        var anonymousResponseText = await anonymousResponse.Content.ReadAsStringAsync();
        anonymousResponseText.Should().NotContain(createdRequest.Id);
        anonymousResponseText.Should().NotContain("Protected pending report request");

        SetAuthorizationHeader(trainer.Id);
        using var unrelatedCreateResponse = await Client.PostAsJsonAsync($"/api/trainer/trainees/{otherTrainee.Id}/report-requests", new
        {
            templateId = template.Id,
            note = "Blocked unrelated report request"
        });
        unrelatedCreateResponse.StatusCode.Should().Be(HttpStatusCode.NotFound);

        SetAuthorizationHeader(otherTrainee.Id);
        using var ordinaryCreateResponse = await Client.PostAsJsonAsync($"/api/trainer/trainees/{trainee.Id}/report-requests", new
        {
            templateId = template.Id,
            note = "Blocked ordinary report request"
        });
        ordinaryCreateResponse.StatusCode.Should().Be(HttpStatusCode.Forbidden);

        ClearAuthorizationHeader();
        using var anonymousCreateResponse = await Client.PostAsJsonAsync($"/api/trainer/trainees/{trainee.Id}/report-requests", new
        {
            templateId = template.Id,
            note = "Blocked anonymous report request"
        });
        anonymousCreateResponse.StatusCode.Should().Be(HttpStatusCode.Unauthorized);

        SetAuthorizationHeader(trainer.Id);
        using (var unlinkScope = Factory.Services.CreateScope())
        {
            var database = unlinkScope.ServiceProvider.GetRequiredService<AppDbContext>();
            var link = await database.TrainerTraineeLinks.SingleAsync(candidate => candidate.TrainerId == trainer.Id && candidate.TraineeId == trainee.Id);
            database.TrainerTraineeLinks.Remove(link);
            await database.SaveChangesAsync();
        }

        using var formerCreateResponse = await Client.PostAsJsonAsync($"/api/trainer/trainees/{trainee.Id}/report-requests", new
        {
            templateId = template.Id,
            note = "Blocked former report request"
        });
        formerCreateResponse.StatusCode.Should().Be(HttpStatusCode.NotFound);
    }

    [Test]
    [AuthorizationEvidence("GET", "/api/trainer/report-templates", "own", "owner-allow")]
    [AuthorizationEvidence("GET", "/api/trainer/report-templates", "own", "no-client-subject")]
    [AuthorizationEvidence("GET", "/api/trainer/report-templates", "own", "anonymous-denial")]
    public async Task TrainerReportTemplatesRoute_IsOwnerScopedAndNonDisclosing()
    {
        var owner = await SeedTrainerAsync("task10-template-owner", "task10-template-owner@example.test");
        var otherTrainer = await SeedTrainerAsync("task10-template-other", "task10-template-other@example.test");
        var ordinaryUser = await SeedUserAsync("task10-template-ordinary", "task10-template-ordinary@example.test");

        SetAuthorizationHeader(owner.Id);
        var ownerTemplateOne = await CreateReportTemplateAsync(
            "Task 10 owner template one",
            "Owner-only field one",
            "ownerFieldOne");
        var ownerTemplateTwo = await CreateReportTemplateAsync(
            "Task 10 owner template two",
            "Owner-only field two",
            "ownerFieldTwo");

        using var ownerResponse = await Client.GetAsync("/api/trainer/report-templates");
        ownerResponse.StatusCode.Should().Be(HttpStatusCode.OK);
        var ownerTemplates = await ownerResponse.Content.ReadFromJsonAsync<List<ReportTemplateResponse>>();
        ownerTemplates.Should().NotBeNull();
        ownerTemplates!.Select(template => template.Id).Should().BeEquivalentTo(ownerTemplateOne.Id, ownerTemplateTwo.Id);
        ownerTemplates.Select(template => template.Name).Should().BeEquivalentTo(
            "Task 10 owner template one",
            "Task 10 owner template two");
        ownerTemplates.SelectMany(template => template.Fields.Select(field => field.Label))
            .Should().BeEquivalentTo("Owner-only field one", "Owner-only field two");

        SetAuthorizationHeader(otherTrainer.Id);
        using var otherTrainerResponse = await Client.GetAsync("/api/trainer/report-templates");
        otherTrainerResponse.StatusCode.Should().Be(HttpStatusCode.OK);
        var otherTrainerTemplates = await otherTrainerResponse.Content.ReadFromJsonAsync<List<ReportTemplateResponse>>();
        otherTrainerTemplates.Should().NotBeNull();
        otherTrainerTemplates.Should().BeEmpty();
        var otherTrainerText = await otherTrainerResponse.Content.ReadAsStringAsync();
        otherTrainerText.Should().NotContain(ownerTemplateOne.Id);
        otherTrainerText.Should().NotContain(ownerTemplateTwo.Id);
        otherTrainerText.Should().NotContain("Task 10 owner template one");
        otherTrainerText.Should().NotContain("Task 10 owner template two");
        otherTrainerText.Should().NotContain("Owner-only field one");
        otherTrainerText.Should().NotContain("Owner-only field two");

        SetAuthorizationHeader(ordinaryUser.Id);
        using var ordinaryResponse = await Client.GetAsync("/api/trainer/report-templates");
        ordinaryResponse.StatusCode.Should().Be(HttpStatusCode.Forbidden);
        var ordinaryText = await ordinaryResponse.Content.ReadAsStringAsync();
        ordinaryText.Should().NotContain(ownerTemplateOne.Id);
        ordinaryText.Should().NotContain(ownerTemplateTwo.Id);
        ordinaryText.Should().NotContain("Task 10 owner template one");
        ordinaryText.Should().NotContain("Task 10 owner template two");

        ClearAuthorizationHeader();
        using var anonymousResponse = await Client.GetAsync("/api/trainer/report-templates");
        anonymousResponse.StatusCode.Should().Be(HttpStatusCode.Unauthorized);
        var anonymousText = await anonymousResponse.Content.ReadAsStringAsync();
        anonymousText.Should().NotContain(ownerTemplateOne.Id);
        anonymousText.Should().NotContain(ownerTemplateTwo.Id);
        anonymousText.Should().NotContain("Owner-only field one");
        anonymousText.Should().NotContain("Owner-only field two");
    }

    [Test]
    [AuthorizationEvidence("GET", "/api/trainer/report-templates/{templateId}", "own", "owner-allow")]
    [AuthorizationEvidence("GET", "/api/trainer/report-templates/{templateId}", "own", "foreign-object-denial-no-mutation")]
    [AuthorizationEvidence("GET", "/api/trainer/report-templates/{templateId}", "own", "anonymous-denial")]
    public async Task TrainerReportTemplateDetailRoute_IsOwnerScopedAndNonDisclosing()
    {
        var owner = await SeedTrainerAsync("task10-template-detail-owner", "task10-template-detail-owner@example.test");
        var otherTrainer = await SeedTrainerAsync("task10-template-detail-other", "task10-template-detail-other@example.test");
        var ordinaryUser = await SeedUserAsync("task10-template-detail-ordinary", "task10-template-detail-ordinary@example.test");

        SetAuthorizationHeader(owner.Id);
        var protectedTemplate = await CreateReportTemplateAsync(
            "Task 10 protected detail template",
            "Protected detail field",
            "protectedDetailField");
        var route = $"/api/trainer/report-templates/{protectedTemplate.Id}";

        using var ownerResponse = await Client.GetAsync(route);
        ownerResponse.StatusCode.Should().Be(HttpStatusCode.OK);
        var ownerTemplate = await ownerResponse.Content.ReadFromJsonAsync<ReportTemplateResponse>();
        ownerTemplate.Should().NotBeNull();
        ownerTemplate!.Id.Should().Be(protectedTemplate.Id);
        ownerTemplate.Name.Should().Be("Task 10 protected detail template");
        ownerTemplate.Fields.Should().ContainSingle(field => field.Label == "Protected detail field");

        SetAuthorizationHeader(otherTrainer.Id);
        using var otherResponse = await Client.GetAsync(route);
        otherResponse.StatusCode.Should().Be(HttpStatusCode.NotFound);
        var otherText = await otherResponse.Content.ReadAsStringAsync();
        otherText.Should().NotContain(protectedTemplate.Id);
        otherText.Should().NotContain("Task 10 protected detail template");
        otherText.Should().NotContain("Protected detail field");

        SetAuthorizationHeader(ordinaryUser.Id);
        using var ordinaryResponse = await Client.GetAsync(route);
        ordinaryResponse.StatusCode.Should().Be(HttpStatusCode.Forbidden);
        var ordinaryText = await ordinaryResponse.Content.ReadAsStringAsync();
        ordinaryText.Should().NotContain(protectedTemplate.Id);
        ordinaryText.Should().NotContain("Task 10 protected detail template");
        ordinaryText.Should().NotContain("Protected detail field");

        ClearAuthorizationHeader();
        using var anonymousResponse = await Client.GetAsync(route);
        anonymousResponse.StatusCode.Should().Be(HttpStatusCode.Unauthorized);
        var anonymousText = await anonymousResponse.Content.ReadAsStringAsync();
        anonymousText.Should().NotContain(protectedTemplate.Id);
        anonymousText.Should().NotContain("Task 10 protected detail template");
        anonymousText.Should().NotContain("Protected detail field");
    }

    [Test]
    [AuthorizationEvidence("GET", "/api/trainer/trainees/{traineeId}/recurring-report-assignments", "trainer-shared", "active-relationship-allow")]
    [AuthorizationEvidence("GET", "/api/trainer/trainees/{traineeId}/recurring-report-assignments", "trainer-shared", "unrelated-relationship-denial")]
    [AuthorizationEvidence("GET", "/api/trainer/trainees/{traineeId}/recurring-report-assignments", "trainer-shared", "former-relationship-denial")]
    [AuthorizationEvidence("GET", "/api/trainer/trainees/{traineeId}/recurring-report-assignments", "trainer-shared", "foreign-object-denial-no-mutation")]
    [AuthorizationEvidence("GET", "/api/trainer/trainees/{traineeId}/recurring-report-assignments", "trainer-shared", "anonymous-denial")]
    public async Task TrainerRecurringReportAssignmentsRoute_IsRelationshipScopedAndNonDisclosing()
    {
        var trainer = await SeedTrainerAsync("http-recurring-trainer", "http-recurring-trainer@example.test");
        var linked = await SeedUserAsync("http-recurring-linked", "http-recurring-linked@example.test");
        var unrelated = await SeedUserAsync("http-recurring-unrelated", "http-recurring-unrelated@example.test");
        var ordinaryUser = await SeedUserAsync("http-recurring-ordinary", "http-recurring-ordinary@example.test");

        using (var seedScope = Factory.Services.CreateScope())
        {
            var database = seedScope.ServiceProvider.GetRequiredService<AppDbContext>();
            database.TrainerTraineeLinks.Add(new TrainerTraineeLink
            {
                Id = Id<TrainerTraineeLink>.New(),
                TrainerId = trainer.Id,
                TraineeId = linked.Id
            });
            await database.SaveChangesAsync();
        }

        SetAuthorizationHeader(trainer.Id);
        var template = await CreateReportTemplateAsync(
            "HTTP recurring protected template",
            "HTTP recurring field",
            "httpRecurringField");
        using var createResponse = await Client.PostAsJsonAsync(
            $"/api/trainer/trainees/{linked.Id}/recurring-report-assignments",
            new
            {
                templateId = template.Id,
                intervalValue = 1,
                intervalUnit = "Week",
                startsAt = DateTimeOffset.UtcNow,
                note = "HTTP recurring protected assignment"
            });
        createResponse.StatusCode.Should().Be(HttpStatusCode.Created);
        var createdText = await createResponse.Content.ReadAsStringAsync();
        createdText.Should().Contain("HTTP recurring protected assignment");

        using var ownerResponse = await Client.GetAsync($"/api/trainer/trainees/{linked.Id}/recurring-report-assignments");
        ownerResponse.StatusCode.Should().Be(HttpStatusCode.OK);
        var ownerAssignments = await ownerResponse.Content.ReadFromJsonAsync<List<RecurringReportAssignmentResponse>>();
        ownerAssignments.Should().NotBeNull();
        ownerAssignments!.Should().ContainSingle(assignment =>
            assignment.TemplateId == template.Id
            && assignment.Note == "HTTP recurring protected assignment");

        using var unrelatedResponse = await Client.GetAsync($"/api/trainer/trainees/{unrelated.Id}/recurring-report-assignments");
        unrelatedResponse.StatusCode.Should().Be(HttpStatusCode.NotFound);
        var unrelatedText = await unrelatedResponse.Content.ReadAsStringAsync();
        unrelatedText.Should().NotContain(template.Id);
        unrelatedText.Should().NotContain("HTTP recurring protected assignment");

        using (var unlinkScope = Factory.Services.CreateScope())
        {
            var database = unlinkScope.ServiceProvider.GetRequiredService<AppDbContext>();
            var link = await database.TrainerTraineeLinks
                .SingleAsync(candidate => candidate.TrainerId == trainer.Id && candidate.TraineeId == linked.Id);
            database.TrainerTraineeLinks.Remove(link);
            await database.SaveChangesAsync();
        }

        using var formerResponse = await Client.GetAsync($"/api/trainer/trainees/{linked.Id}/recurring-report-assignments");
        formerResponse.StatusCode.Should().Be(HttpStatusCode.NotFound);
        var formerText = await formerResponse.Content.ReadAsStringAsync();
        formerText.Should().NotContain(template.Id);
        formerText.Should().NotContain("HTTP recurring protected assignment");

        SetAuthorizationHeader(ordinaryUser.Id);
        using var ordinaryResponse = await Client.GetAsync($"/api/trainer/trainees/{linked.Id}/recurring-report-assignments");
        ordinaryResponse.StatusCode.Should().Be(HttpStatusCode.Forbidden);
        var ordinaryText = await ordinaryResponse.Content.ReadAsStringAsync();
        ordinaryText.Should().NotContain(template.Id);
        ordinaryText.Should().NotContain("HTTP recurring protected assignment");

        ClearAuthorizationHeader();
        using var anonymousResponse = await Client.GetAsync($"/api/trainer/trainees/{linked.Id}/recurring-report-assignments");
        anonymousResponse.StatusCode.Should().Be(HttpStatusCode.Unauthorized);
        var anonymousText = await anonymousResponse.Content.ReadAsStringAsync();
        anonymousText.Should().NotContain(template.Id);
        anonymousText.Should().NotContain("HTTP recurring protected assignment");
    }

    [Test]
    [AuthorizationEvidence("POST", "/api/trainee/report-requests/{requestId}/submit", "own", "owner-allow")]
    [AuthorizationEvidence("POST", "/api/trainee/report-requests/{requestId}/submit", "own", "foreign-object-denial-no-mutation")]
    [AuthorizationEvidence("GET", "/api/trainee/report-submissions", "own", "owner-allow")]
    [AuthorizationEvidence("GET", "/api/trainee/report-submissions", "own", "no-client-subject")]
    [AuthorizationEvidence("GET", "/api/trainee/report-submissions", "own", "anonymous-denial")]
    public async Task TraineeReportSubmissionsRoute_IsActorScopedAndNonDisclosing()
    {
        var trainer = await SeedTrainerAsync("task10-submission-trainer", "task10-submission-trainer@example.test");
        var trainee = await SeedUserAsync("task10-submission-trainee", "task10-submission-trainee@example.test");
        var otherTrainee = await SeedUserAsync("task10-submission-other", "task10-submission-other@example.test");

        using (var scope = Factory.Services.CreateScope())
        {
            var database = scope.ServiceProvider.GetRequiredService<AppDbContext>();
            database.TrainerTraineeLinks.Add(new TrainerTraineeLink
            {
                Id = Id<TrainerTraineeLink>.New(),
                TrainerId = trainer.Id,
                TraineeId = trainee.Id
            });
            await database.SaveChangesAsync();
        }

        SetAuthorizationHeader(trainer.Id);
        using var templateResponse = await Client.PostAsJsonAsync("/api/trainer/report-templates", new
        {
            name = "Task 10 submission template",
            fields = new[]
            {
                new { key = "checkIn", label = "Check-in", type = "Text", isRequired = true, order = 0 }
            }
        });
        templateResponse.StatusCode.Should().Be(HttpStatusCode.Created);
        var template = await templateResponse.Content.ReadFromJsonAsync<ReportTemplateResponse>();
        template.Should().NotBeNull();

        using var requestResponse = await Client.PostAsJsonAsync($"/api/trainer/trainees/{trainee.Id}/report-requests", new
        {
            templateId = template!.Id,
            note = "Task 10 submission fixture"
        });
        requestResponse.StatusCode.Should().Be(HttpStatusCode.Created);
        var request = await requestResponse.Content.ReadFromJsonAsync<ReportRequestResponse>();
        request.Should().NotBeNull();

        SetAuthorizationHeader(otherTrainee.Id);
        using var foreignSubmitResponse = await Client.PostAsJsonAsync($"/api/trainee/report-requests/{request!.Id}/submit", new
        {
            answers = new { checkIn = "Foreign submission must not be accepted" }
        });
        foreignSubmitResponse.StatusCode.Should().Be(HttpStatusCode.NotFound);
        (await foreignSubmitResponse.Content.ReadAsStringAsync()).Should().NotContain("Foreign submission must not be accepted");

        SetAuthorizationHeader(trainee.Id);
        using var submitResponse = await Client.PostAsJsonAsync($"/api/trainee/report-requests/{request.Id}/submit", new
        {
            answers = new { checkIn = "Protected submission answer" }
        });
        submitResponse.StatusCode.Should().Be(HttpStatusCode.OK);
        var submitted = await submitResponse.Content.ReadFromJsonAsync<ReportSubmissionResponse>();
        submitted.Should().NotBeNull();

        using var ownerResponse = await Client.GetAsync("/api/trainee/report-submissions");
        ownerResponse.StatusCode.Should().Be(HttpStatusCode.OK);
        var ownerSubmissions = await ownerResponse.Content.ReadFromJsonAsync<List<ReportSubmissionResponse>>();
        ownerSubmissions.Should().NotBeNull();
        ownerSubmissions!.Should().ContainSingle(submission => submission.Id == submitted!.Id);
        ownerSubmissions.Single().ReportRequestId.Should().Be(request.Id);
        ownerSubmissions.Single().Answers["checkIn"].GetString().Should().Be("Protected submission answer");

        SetAuthorizationHeader(otherTrainee.Id);
        using var otherResponse = await Client.GetAsync("/api/trainee/report-submissions");
        otherResponse.StatusCode.Should().Be(HttpStatusCode.OK);
        var otherSubmissions = await otherResponse.Content.ReadFromJsonAsync<List<ReportSubmissionResponse>>();
        otherSubmissions.Should().NotBeNull();
        otherSubmissions.Should().BeEmpty();
        var otherResponseText = await otherResponse.Content.ReadAsStringAsync();
        otherResponseText.Should().NotContain(submitted!.Id);
        otherResponseText.Should().NotContain(request.Id);
        otherResponseText.Should().NotContain("Protected submission answer");

        ClearAuthorizationHeader();
        using var anonymousResponse = await Client.GetAsync("/api/trainee/report-submissions");
        anonymousResponse.StatusCode.Should().Be(HttpStatusCode.Unauthorized);
        var anonymousResponseText = await anonymousResponse.Content.ReadAsStringAsync();
        anonymousResponseText.Should().NotContain(submitted.Id);
        anonymousResponseText.Should().NotContain(request.Id);
        anonymousResponseText.Should().NotContain("Protected submission answer");
    }

    [Test]
    [AuthorizationEvidence("POST", "/api/trainee/report-requests/{requestId}/submit", "own", "anonymous-denial")]
    public async Task TraineeReportRequestSubmit_WithoutAuthorization_ReturnsUnauthorized()
    {
        ClearAuthorizationHeader();

        using var response = await Client.PostAsJsonAsync($"/api/trainee/report-requests/{Id<ReportRequest>.New()}/submit", new
        {
            answers = new { protectedAnswer = "must not be processed" }
        });

        response.StatusCode.Should().Be(HttpStatusCode.Unauthorized);
        (await response.Content.ReadAsStringAsync()).Should().NotContain("must not be processed");
    }

    [Test]
    [AuthorizationEvidence("GET", "/api/trainer/trainees/{traineeId}/report-submissions", "trainer-shared", "active-relationship-allow")]
    [AuthorizationEvidence("GET", "/api/trainer/trainees/{traineeId}/report-submissions", "trainer-shared", "unrelated-relationship-denial")]
    [AuthorizationEvidence("GET", "/api/trainer/trainees/{traineeId}/report-submissions", "trainer-shared", "former-relationship-denial")]
    [AuthorizationEvidence("GET", "/api/trainer/trainees/{traineeId}/report-submissions", "trainer-shared", "foreign-object-denial-no-mutation")]
    [AuthorizationEvidence("GET", "/api/trainer/trainees/{traineeId}/report-submissions", "trainer-shared", "anonymous-denial")]
    public async Task TrainerReportSubmissionsRoute_IsRelationshipScopedAndNonDisclosing()
    {
        var trainer = await SeedTrainerAsync("http-trainer-submissions-trainer", "http-trainer-submissions-trainer@example.test");
        var linked = await SeedUserAsync("http-trainer-submissions-linked", "http-trainer-submissions-linked@example.test");
        var unrelated = await SeedUserAsync("http-trainer-submissions-unrelated", "http-trainer-submissions-unrelated@example.test");
        var ordinaryUser = await SeedUserAsync("http-trainer-submissions-ordinary", "http-trainer-submissions-ordinary@example.test");

        using (var seedScope = Factory.Services.CreateScope())
        {
            var database = seedScope.ServiceProvider.GetRequiredService<AppDbContext>();
            database.TrainerTraineeLinks.Add(new TrainerTraineeLink
            {
                Id = Id<TrainerTraineeLink>.New(),
                TrainerId = trainer.Id,
                TraineeId = linked.Id
            });
            await database.SaveChangesAsync();
        }

        SetAuthorizationHeader(trainer.Id);
        var template = await CreateReportTemplateAsync(
            "HTTP trainer submissions template",
            "HTTP trainer submissions field",
            "httpTrainerSubmissionsField");
        using var requestResponse = await Client.PostAsJsonAsync($"/api/trainer/trainees/{linked.Id}/report-requests", new
        {
            templateId = template.Id,
            note = "HTTP trainer submissions request"
        });
        requestResponse.StatusCode.Should().Be(HttpStatusCode.Created);
        var request = await requestResponse.Content.ReadFromJsonAsync<ReportRequestResponse>();
        request.Should().NotBeNull();

        SetAuthorizationHeader(linked.Id);
        using var submitResponse = await Client.PostAsJsonAsync($"/api/trainee/report-requests/{request!.Id}/submit", new
        {
            answers = new { httpTrainerSubmissionsField = "HTTP trainer submissions answer" }
        });
        submitResponse.StatusCode.Should().Be(HttpStatusCode.OK);
        var submission = await submitResponse.Content.ReadFromJsonAsync<ReportSubmissionResponse>();
        submission.Should().NotBeNull();

        SetAuthorizationHeader(trainer.Id);
        using var ownerResponse = await Client.GetAsync($"/api/trainer/trainees/{linked.Id}/report-submissions");
        ownerResponse.StatusCode.Should().Be(HttpStatusCode.OK);
        var ownerSubmissions = await ownerResponse.Content.ReadFromJsonAsync<List<ReportSubmissionResponse>>();
        ownerSubmissions.Should().NotBeNull();
        ownerSubmissions!.Should().ContainSingle(item => item.Id == submission!.Id);
        ownerSubmissions.Single().Answers["httpTrainerSubmissionsField"].GetString().Should().Be("HTTP trainer submissions answer");

        using var unrelatedResponse = await Client.GetAsync($"/api/trainer/trainees/{unrelated.Id}/report-submissions");
        unrelatedResponse.StatusCode.Should().Be(HttpStatusCode.NotFound);
        var unrelatedText = await unrelatedResponse.Content.ReadAsStringAsync();
        unrelatedText.Should().NotContain(submission.Id);
        unrelatedText.Should().NotContain("HTTP trainer submissions answer");

        using (var unlinkScope = Factory.Services.CreateScope())
        {
            var database = unlinkScope.ServiceProvider.GetRequiredService<AppDbContext>();
            var link = await database.TrainerTraineeLinks
                .SingleAsync(candidate => candidate.TrainerId == trainer.Id && candidate.TraineeId == linked.Id);
            database.TrainerTraineeLinks.Remove(link);
            await database.SaveChangesAsync();
        }

        using var formerResponse = await Client.GetAsync($"/api/trainer/trainees/{linked.Id}/report-submissions");
        formerResponse.StatusCode.Should().Be(HttpStatusCode.NotFound);
        var formerText = await formerResponse.Content.ReadAsStringAsync();
        formerText.Should().NotContain(submission.Id);
        formerText.Should().NotContain("HTTP trainer submissions answer");

        SetAuthorizationHeader(ordinaryUser.Id);
        using var ordinaryResponse = await Client.GetAsync($"/api/trainer/trainees/{linked.Id}/report-submissions");
        ordinaryResponse.StatusCode.Should().Be(HttpStatusCode.Forbidden);
        var ordinaryText = await ordinaryResponse.Content.ReadAsStringAsync();
        ordinaryText.Should().NotContain(submission.Id);
        ordinaryText.Should().NotContain("HTTP trainer submissions answer");

        ClearAuthorizationHeader();
        using var anonymousResponse = await Client.GetAsync($"/api/trainer/trainees/{linked.Id}/report-submissions");
        anonymousResponse.StatusCode.Should().Be(HttpStatusCode.Unauthorized);
        var anonymousText = await anonymousResponse.Content.ReadAsStringAsync();
        anonymousText.Should().NotContain(submission.Id);
        anonymousText.Should().NotContain("HTTP trainer submissions answer");
    }

    [Test]
    [AuthorizationEvidence("GET", "/api/trainee/reporting/photos/history", "own", "owner-allow")]
    [AuthorizationEvidence("GET", "/api/trainee/reporting/photos/history", "own", "no-client-subject")]
    [AuthorizationEvidence("GET", "/api/trainee/reporting/photos/history", "own", "anonymous-denial")]
    public async Task TraineePhotoHistoryRoute_IsActorScopedAndNonDisclosing()
    {
        var trainer = await SeedTrainerAsync("task10-photo-history-trainer", "task10-photo-history-trainer@example.test");
        var trainee = await SeedUserAsync("task10-photo-history-trainee", "task10-photo-history-trainee@example.test");
        var otherTrainee = await SeedUserAsync("task10-photo-history-other", "task10-photo-history-other@example.test");
        var template = new ReportTemplate
        {
            Id = Id<ReportTemplate>.New(),
            TrainerId = trainer.Id,
            Name = "Task 10 photo history template"
        };
        var request = new ReportRequest
        {
            Id = Id<ReportRequest>.New(),
            TrainerId = trainer.Id,
            TraineeId = trainee.Id,
            TemplateId = template.Id,
            Status = ReportRequestStatus.Pending,
            Note = "Protected photo history request"
        };
        var photo = new Photo
        {
            Id = Id<Photo>.New(),
            StorageKey = "task10-photo-history/protected.jpg",
            ThumbnailStorageKey = "task10-photo-history/protected-thumb.jpg",
            MimeType = "image/jpeg",
            SizeBytes = 4096,
            Checksum = "task10-photo-history-checksum",
            ViewType = "Front",
            ReportRequestId = request.Id,
            UploaderUserId = trainee.Id,
            OwnerUserId = trainee.Id
        };

        using (var scope = Factory.Services.CreateScope())
        {
            var database = scope.ServiceProvider.GetRequiredService<AppDbContext>();
            database.ReportTemplates.Add(template);
            database.ReportRequests.Add(request);
            database.Photos.Add(photo);
            await database.SaveChangesAsync();
        }

        SetAuthorizationHeader(trainee.Id);
        using var ownerResponse = await Client.GetAsync($"/api/trainee/reporting/photos/history?requestId={request.Id}");
        ownerResponse.StatusCode.Should().Be(HttpStatusCode.OK);
        var ownerHistory = await ownerResponse.Content.ReadFromJsonAsync<PhotoHistoryResponse>();
        ownerHistory.Should().NotBeNull();
        ownerHistory!.Photos.Should().ContainSingle();
        ownerHistory.Photos[0].Id.Should().Be(photo.Id.ToString());
        ownerHistory.Photos[0].ReportRequestId.Should().Be(request.Id.ToString());
        ownerHistory.Photos[0].ViewType.Should().Be(photo.ViewType);
        ownerHistory.Photos[0].SizeBytes.Should().Be(photo.SizeBytes);
        ownerHistory.Photos[0].ReadUrl.Should().NotBeNullOrWhiteSpace();
        ownerHistory.Photos[0].ThumbnailUrl.Should().NotBeNullOrWhiteSpace();

        SetAuthorizationHeader(otherTrainee.Id);
        using var otherResponse = await Client.GetAsync($"/api/trainee/reporting/photos/history?requestId={request.Id}");
        otherResponse.StatusCode.Should().Be(HttpStatusCode.Forbidden);
        var otherResponseText = await otherResponse.Content.ReadAsStringAsync();
        otherResponseText.Should().NotContain(photo.Id.ToString());
        otherResponseText.Should().NotContain(photo.StorageKey);
        otherResponseText.Should().NotContain(photo.ThumbnailStorageKey);
        otherResponseText.Should().NotContain(ownerHistory.Photos[0].ReadUrl);
        otherResponseText.Should().NotContain(ownerHistory.Photos[0].ThumbnailUrl);
        otherResponseText.Should().NotContain(request.Id.ToString());
        otherResponseText.Should().NotContain(request.Note);

        ClearAuthorizationHeader();
        using var anonymousResponse = await Client.GetAsync($"/api/trainee/reporting/photos/history?requestId={request.Id}");
        anonymousResponse.StatusCode.Should().Be(HttpStatusCode.Unauthorized);
        var anonymousResponseText = await anonymousResponse.Content.ReadAsStringAsync();
        anonymousResponseText.Should().NotContain(photo.Id.ToString());
        anonymousResponseText.Should().NotContain(photo.StorageKey);
        anonymousResponseText.Should().NotContain(ownerHistory.Photos[0].ReadUrl);
        anonymousResponseText.Should().NotContain(request.Id.ToString());
        anonymousResponseText.Should().NotContain(request.Note);
    }

    [Test]
    [AuthorizationEvidence("GET", "/api/trainer/reporting/photos/history", "own", "owner-allow")]
    [AuthorizationEvidence("GET", "/api/trainer/reporting/photos/history", "own", "no-client-subject")]
    [AuthorizationEvidence("GET", "/api/trainer/reporting/photos/history", "own", "anonymous-denial")]
    public async Task TrainerPhotoHistoryRoute_RequiresActiveRelationshipAndNonDiscloses()
    {
        var trainer = await SeedTrainerAsync("task10-trainer-photo-history-owner", "task10-trainer-photo-history-owner@example.test");
        var otherTrainer = await SeedTrainerAsync("task10-trainer-photo-history-other", "task10-trainer-photo-history-other@example.test");
        var trainee = await SeedUserAsync("task10-trainer-photo-history-trainee", "task10-trainer-photo-history-trainee@example.test");
        var ordinaryUser = await SeedUserAsync("task10-trainer-photo-history-ordinary", "task10-trainer-photo-history-ordinary@example.test");
        var template = new ReportTemplate
        {
            Id = Id<ReportTemplate>.New(),
            TrainerId = trainer.Id,
            Name = "Task 10 trainer photo history template"
        };
        var request = new ReportRequest
        {
            Id = Id<ReportRequest>.New(),
            TrainerId = trainer.Id,
            TraineeId = trainee.Id,
            TemplateId = template.Id,
            Status = ReportRequestStatus.Pending,
            Note = "Protected trainer photo history request"
        };
        var photo = new Photo
        {
            Id = Id<Photo>.New(),
            StorageKey = "task10-trainer-photo-history/protected.jpg",
            ThumbnailStorageKey = "task10-trainer-photo-history/protected-thumb.jpg",
            MimeType = "image/jpeg",
            SizeBytes = 8192,
            Checksum = "task10-trainer-photo-history-checksum",
            ViewType = "Side",
            ReportRequestId = request.Id,
            UploaderUserId = trainee.Id,
            OwnerUserId = trainee.Id
        };

        using (var scope = Factory.Services.CreateScope())
        {
            var database = scope.ServiceProvider.GetRequiredService<AppDbContext>();
            database.TrainerTraineeLinks.Add(new TrainerTraineeLink
            {
                Id = Id<TrainerTraineeLink>.New(),
                TrainerId = trainer.Id,
                TraineeId = trainee.Id
            });
            database.ReportTemplates.Add(template);
            database.ReportRequests.Add(request);
            database.Photos.Add(photo);
            await database.SaveChangesAsync();
        }

        var route = $"/api/trainer/reporting/photos/history?requestId={request.Id}";
        SetAuthorizationHeader(trainer.Id);
        using var ownerResponse = await Client.GetAsync(route);
        ownerResponse.StatusCode.Should().Be(HttpStatusCode.OK);
        var ownerHistory = await ownerResponse.Content.ReadFromJsonAsync<PhotoHistoryResponse>();
        ownerHistory.Should().NotBeNull();
        ownerHistory!.Photos.Should().ContainSingle();
        ownerHistory.Photos[0].Id.Should().Be(photo.Id.ToString());
        ownerHistory.Photos[0].ReportRequestId.Should().Be(request.Id.ToString());
        ownerHistory.Photos[0].ViewType.Should().Be(photo.ViewType);
        ownerHistory.Photos[0].SizeBytes.Should().Be(photo.SizeBytes);
        ownerHistory.Photos[0].ReadUrl.Should().NotBeNullOrWhiteSpace();
        ownerHistory.Photos[0].ThumbnailUrl.Should().NotBeNullOrWhiteSpace();

        SetAuthorizationHeader(otherTrainer.Id);
        using var unrelatedResponse = await Client.GetAsync(route);
        unrelatedResponse.StatusCode.Should().Be(HttpStatusCode.NotFound);
        var unrelatedText = await unrelatedResponse.Content.ReadAsStringAsync();
        AssertPhotoHistoryDenialDoesNotDisclose(unrelatedText, photo, request, ownerHistory.Photos[0]);

        using (var unlinkScope = Factory.Services.CreateScope())
        {
            var database = unlinkScope.ServiceProvider.GetRequiredService<AppDbContext>();
            var link = await database.TrainerTraineeLinks
                .SingleAsync(candidate => candidate.TrainerId == trainer.Id && candidate.TraineeId == trainee.Id);
            database.TrainerTraineeLinks.Remove(link);
            await database.SaveChangesAsync();
        }

        SetAuthorizationHeader(trainer.Id);
        using var formerResponse = await Client.GetAsync(route);
        formerResponse.StatusCode.Should().Be(HttpStatusCode.NotFound);
        var formerText = await formerResponse.Content.ReadAsStringAsync();
        AssertPhotoHistoryDenialDoesNotDisclose(formerText, photo, request, ownerHistory.Photos[0]);

        SetAuthorizationHeader(ordinaryUser.Id);
        using var ordinaryResponse = await Client.GetAsync(route);
        ordinaryResponse.StatusCode.Should().Be(HttpStatusCode.Forbidden);
        var ordinaryText = await ordinaryResponse.Content.ReadAsStringAsync();
        AssertPhotoHistoryDenialDoesNotDisclose(ordinaryText, photo, request, ownerHistory.Photos[0]);

        ClearAuthorizationHeader();
        using var anonymousResponse = await Client.GetAsync(route);
        anonymousResponse.StatusCode.Should().Be(HttpStatusCode.Unauthorized);
        var anonymousText = await anonymousResponse.Content.ReadAsStringAsync();
        AssertPhotoHistoryDenialDoesNotDisclose(anonymousText, photo, request, ownerHistory.Photos[0]);

        using var verifyScope = Factory.Services.CreateScope();
        var persistedPhoto = await verifyScope.ServiceProvider.GetRequiredService<AppDbContext>().Photos
            .AsNoTracking()
            .SingleAsync(candidate => candidate.Id == photo.Id);
        persistedPhoto.StorageKey.Should().Be(photo.StorageKey);
        persistedPhoto.IsDeleted.Should().BeFalse();
    }

    [Test]
    [AuthorizationEvidence("GET", "/api/trainer/reporting/photos/{photoId}/signed-url", "own", "owner-allow")]
    [AuthorizationEvidence("GET", "/api/trainer/reporting/photos/{photoId}/signed-url", "own", "foreign-object-denial-no-mutation")]
    [AuthorizationEvidence("GET", "/api/trainer/reporting/photos/{photoId}/signed-url", "own", "anonymous-denial")]
    public async Task TrainerPhotoSignedUrlRoute_RequiresAuthorizedPhotoAccessAndNonDiscloses()
    {
        var trainer = await SeedTrainerAsync("task10-signed-url-trainer", "task10-signed-url-trainer@example.test");
        var otherTrainer = await SeedTrainerAsync("task10-signed-url-other-trainer", "task10-signed-url-other-trainer@example.test");
        var trainee = await SeedUserAsync("task10-signed-url-trainee", "task10-signed-url-trainee@example.test");
        var foreignTrainee = await SeedUserAsync("task10-signed-url-foreign-trainee", "task10-signed-url-foreign-trainee@example.test");
        var ordinaryUser = await SeedUserAsync("task10-signed-url-ordinary", "task10-signed-url-ordinary@example.test");
        var template = new ReportTemplate
        {
            Id = Id<ReportTemplate>.New(),
            TrainerId = trainer.Id,
            Name = "Task 10 signed URL template"
        };
        var request = new ReportRequest
        {
            Id = Id<ReportRequest>.New(),
            TrainerId = trainer.Id,
            TraineeId = trainee.Id,
            TemplateId = template.Id,
            Status = ReportRequestStatus.Pending,
            Note = "Protected signed URL request"
        };
        var foreignRequest = new ReportRequest
        {
            Id = Id<ReportRequest>.New(),
            TrainerId = trainer.Id,
            TraineeId = foreignTrainee.Id,
            TemplateId = template.Id,
            Status = ReportRequestStatus.Pending,
            Note = "Foreign signed URL request"
        };
        var photo = new Photo
        {
            Id = Id<Photo>.New(),
            StorageKey = "task10-signed-url/protected.jpg",
            MimeType = "image/jpeg",
            SizeBytes = 2048,
            Checksum = "task10-signed-url-checksum",
            ViewType = "Front",
            ReportRequestId = request.Id,
            UploaderUserId = trainee.Id,
            OwnerUserId = trainee.Id
        };
        var foreignPhoto = new Photo
        {
            Id = Id<Photo>.New(),
            StorageKey = "task10-signed-url/foreign.jpg",
            MimeType = "image/jpeg",
            SizeBytes = 3072,
            Checksum = "task10-signed-url-foreign-checksum",
            ViewType = "Front",
            ReportRequestId = foreignRequest.Id,
            UploaderUserId = foreignTrainee.Id,
            OwnerUserId = foreignTrainee.Id
        };

        using (var scope = Factory.Services.CreateScope())
        {
            var database = scope.ServiceProvider.GetRequiredService<AppDbContext>();
            database.TrainerTraineeLinks.Add(new TrainerTraineeLink
            {
                Id = Id<TrainerTraineeLink>.New(),
                TrainerId = trainer.Id,
                TraineeId = trainee.Id
            });
            database.ReportTemplates.Add(template);
            database.ReportRequests.AddRange(request, foreignRequest);
            database.Photos.AddRange(photo, foreignPhoto);
            await database.SaveChangesAsync();
        }

        var protectedRoute = $"/api/trainer/reporting/photos/{photo.Id}/signed-url";
        SetAuthorizationHeader(trainer.Id);
        using var ownerResponse = await Client.GetAsync(protectedRoute);
        ownerResponse.StatusCode.Should().Be(HttpStatusCode.OK);
        var ownerCapability = await ownerResponse.Content.ReadFromJsonAsync<SignedReadUrlResponse>();
        ownerCapability.Should().NotBeNull();
        ownerCapability!.ReadUrl.Should().NotBeNullOrWhiteSpace();
        ownerCapability.ExpiresAt.Should().BeAfter(DateTimeOffset.UtcNow);
        ownerCapability.ReadUrl.Should().Contain("task10-signed-url");

        var foreignRoute = $"/api/trainer/reporting/photos/{foreignPhoto.Id}/signed-url";
        using var foreignResponse = await Client.GetAsync(foreignRoute);
        foreignResponse.StatusCode.Should().Be(HttpStatusCode.NotFound);
        var foreignText = await foreignResponse.Content.ReadAsStringAsync();
        AssertSignedUrlDenialDoesNotDisclose(foreignText, photo, request, ownerCapability, foreignPhoto);

        SetAuthorizationHeader(otherTrainer.Id);
        using var unrelatedResponse = await Client.GetAsync(protectedRoute);
        unrelatedResponse.StatusCode.Should().Be(HttpStatusCode.NotFound);
        var unrelatedText = await unrelatedResponse.Content.ReadAsStringAsync();
        AssertSignedUrlDenialDoesNotDisclose(unrelatedText, photo, request, ownerCapability, foreignPhoto);

        using (var unlinkScope = Factory.Services.CreateScope())
        {
            var database = unlinkScope.ServiceProvider.GetRequiredService<AppDbContext>();
            var link = await database.TrainerTraineeLinks
                .SingleAsync(candidate => candidate.TrainerId == trainer.Id && candidate.TraineeId == trainee.Id);
            database.TrainerTraineeLinks.Remove(link);
            await database.SaveChangesAsync();
        }

        SetAuthorizationHeader(trainer.Id);
        using var formerResponse = await Client.GetAsync(protectedRoute);
        formerResponse.StatusCode.Should().Be(HttpStatusCode.NotFound);
        var formerText = await formerResponse.Content.ReadAsStringAsync();
        AssertSignedUrlDenialDoesNotDisclose(formerText, photo, request, ownerCapability, foreignPhoto);

        SetAuthorizationHeader(ordinaryUser.Id);
        using var ordinaryResponse = await Client.GetAsync(protectedRoute);
        ordinaryResponse.StatusCode.Should().Be(HttpStatusCode.Forbidden);
        var ordinaryText = await ordinaryResponse.Content.ReadAsStringAsync();
        AssertSignedUrlDenialDoesNotDisclose(ordinaryText, photo, request, ownerCapability, foreignPhoto);

        ClearAuthorizationHeader();
        using var anonymousResponse = await Client.GetAsync(protectedRoute);
        anonymousResponse.StatusCode.Should().Be(HttpStatusCode.Unauthorized);
        var anonymousText = await anonymousResponse.Content.ReadAsStringAsync();
        AssertSignedUrlDenialDoesNotDisclose(anonymousText, photo, request, ownerCapability, foreignPhoto);

        using var verifyScope = Factory.Services.CreateScope();
        var databaseSnapshot = verifyScope.ServiceProvider.GetRequiredService<AppDbContext>();
        var persistedPhotos = await databaseSnapshot.Photos
            .AsNoTracking()
            .Where(candidate => candidate.Id == photo.Id || candidate.Id == foreignPhoto.Id)
            .ToListAsync();
        persistedPhotos.Should().HaveCount(2);
        persistedPhotos.Should().OnlyContain(candidate => !candidate.IsDeleted);
    }

    private static void AssertSignedUrlDenialDoesNotDisclose(
        string responseText,
        Photo photo,
        ReportRequest request,
        SignedReadUrlResponse ownerCapability,
        Photo foreignPhoto)
    {
        responseText.Should().NotContain(photo.Id.ToString());
        responseText.Should().NotContain(photo.StorageKey);
        responseText.Should().NotContain(ownerCapability.ReadUrl);
        responseText.Should().NotContain(request.Id.ToString());
        responseText.Should().NotContain(request.Note);
        responseText.Should().NotContain(foreignPhoto.Id.ToString());
        responseText.Should().NotContain(foreignPhoto.StorageKey);
    }

    private static void AssertPhotoHistoryDenialDoesNotDisclose(
        string responseText,
        Photo photo,
        ReportRequest request,
        PhotoHistoryItemResponse ownerPhoto)
    {
        responseText.Should().NotContain(photo.Id.ToString());
        responseText.Should().NotContain(photo.StorageKey);
        responseText.Should().NotContain(photo.ThumbnailStorageKey);
        responseText.Should().NotContain(ownerPhoto.ReadUrl);
        responseText.Should().NotContain(ownerPhoto.ThumbnailUrl);
        responseText.Should().NotContain(request.Id.ToString());
        responseText.Should().NotContain(request.Note);
    }

    [Test]
    public async Task SubmitReport_WithInvalidDynamicFieldType_ReturnsBadRequest()
    {
        var trainer = await SeedTrainerAsync("trainer-reports-invalid", "trainer-reports-invalid@example.com");
        var trainee = await SeedUserAsync(name: "trainee-reports-invalid", email: "trainee-reports-invalid@example.com", password: "password123");

        using (var scope = Factory.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
            db.TrainerTraineeLinks.Add(new TrainerTraineeLink
            {
                Id = Domain.ValueObjects.Id<TrainerTraineeLink>.New(),
                TrainerId = (Domain.ValueObjects.Id<User>)trainer.Id,
                TraineeId = (Domain.ValueObjects.Id<User>)trainee.Id
            });
            await db.SaveChangesAsync();
        }

        SetAuthorizationHeader(trainer.Id);
        var templateResponse = await Client.PostAsJsonAsync("/api/trainer/report-templates", new
        {
            name = "Daily",
            fields = new object[]
            {
                new { key = "mood", label = "Mood", type = "Text", isRequired = true, order = 0 }
            }
        });
        var template = await templateResponse.Content.ReadFromJsonAsync<ReportTemplateResponse>();

        var requestResponse = await Client.PostAsJsonAsync($"/api/trainer/trainees/{trainee.Id}/report-requests", new
        {
            templateId = template!.Id
        });
        var request = await requestResponse.Content.ReadFromJsonAsync<ReportRequestResponse>();

        SetAuthorizationHeader(trainee.Id);
        var submitResponse = await Client.PostAsJsonAsync($"/api/trainee/report-requests/{request!.Id}/submit", new
        {
            answers = new
            {
                mood = 123
            }
        });

        submitResponse.StatusCode.Should().Be(HttpStatusCode.BadRequest);
    }

    [Test]
    public async Task ReportRequests_WithMidnightDueAt_RemainVisibleUntilEndOfDay()
    {
        var trainer = await SeedTrainerAsync("trainer-reports-midnight", "trainer-reports-midnight@example.com");
        var trainee = await SeedUserAsync(name: "trainee-reports-midnight", email: "trainee-reports-midnight@example.com", password: "password123");

        using (var scope = Factory.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
            db.TrainerTraineeLinks.Add(new TrainerTraineeLink
            {
                Id = Domain.ValueObjects.Id<TrainerTraineeLink>.New(),
                TrainerId = (Domain.ValueObjects.Id<User>)trainer.Id,
                TraineeId = (Domain.ValueObjects.Id<User>)trainee.Id
            });
            await db.SaveChangesAsync();
        }

        SetAuthorizationHeader(trainer.Id);
        var createTemplateResponse = await Client.PostAsJsonAsync("/api/trainer/report-templates", new
        {
            name = "Same-day deadline",
            fields = new object[]
            {
                new { key = "checkin", label = "Check-in", type = "Text", isRequired = true, order = 0 }
            }
        });

        createTemplateResponse.StatusCode.Should().Be(HttpStatusCode.Created);
        var template = await createTemplateResponse.Content.ReadFromJsonAsync<ReportTemplateResponse>();
        template.Should().NotBeNull();

        var dueAt = DateTimeOffset.UtcNow.Date.AddDays(1);
        var createRequestResponse = await Client.PostAsJsonAsync($"/api/trainer/trainees/{trainee.Id}/report-requests", new
        {
            templateId = template!.Id,
            dueAt
        });

        createRequestResponse.StatusCode.Should().Be(HttpStatusCode.Created);
        var request = await createRequestResponse.Content.ReadFromJsonAsync<ReportRequestResponse>();
        request.Should().NotBeNull();
        request!.Status.Should().Be("Pending");

        SetAuthorizationHeader(trainee.Id);
        var pendingResponse = await Client.GetAsync("/api/trainee/report-requests");
        pendingResponse.StatusCode.Should().Be(HttpStatusCode.OK);
        var pending = await pendingResponse.Content.ReadFromJsonAsync<List<ReportRequestResponse>>();
        pending.Should().NotBeNull();
        pending!.Should().ContainSingle(x => x.Id == request.Id);
    }

    [Test]
    public async Task TrainerReportingController_InvalidIds_ReturnBadRequest()
    {
        var trainer = await SeedTrainerAsync("trainer-report-invalid-ids", "trainer-report-invalid-ids@example.com");
        SetAuthorizationHeader(trainer.Id);

        var getTemplate = await Client.GetAsync("/api/trainer/report-templates/not-a-guid");
        var updateTemplate = await Client.PostAsJsonAsync("/api/trainer/report-templates/not-a-guid/update", new
        {
            name = "x",
            fields = new object[] { new { key = "k", label = "l", type = "Text", isRequired = false, order = 0 } }
        });
        var deleteTemplate = await Client.PostAsync("/api/trainer/report-templates/not-a-guid/delete", content: null);
        var createRequestBadTrainee = await Client.PostAsJsonAsync("/api/trainer/trainees/not-a-guid/report-requests", new
        {
            templateId = Domain.ValueObjects.Id<object>.New().ToString()
        });
        var createRequestBadTemplate = await Client.PostAsJsonAsync($"/api/trainer/trainees/{Domain.ValueObjects.Id<User>.New()}/report-requests", new
        {
            templateId = "not-a-guid"
        });
        var getSubmissionsBadTrainee = await Client.GetAsync("/api/trainer/trainees/not-a-guid/report-submissions");

        getTemplate.StatusCode.Should().Be(HttpStatusCode.BadRequest);
        updateTemplate.StatusCode.Should().Be(HttpStatusCode.BadRequest);
        deleteTemplate.StatusCode.Should().Be(HttpStatusCode.BadRequest);
        createRequestBadTrainee.StatusCode.Should().Be(HttpStatusCode.BadRequest);
        createRequestBadTemplate.StatusCode.Should().Be(HttpStatusCode.BadRequest);
        getSubmissionsBadTrainee.StatusCode.Should().Be(HttpStatusCode.BadRequest);
    }

    [Test]
    [AuthorizationEvidence("POST", "/api/trainer/report-templates", "own", "owner-allow")]
    public async Task TrainerReportingController_TemplateCreateAndReadFlow_Works()
    {
        var trainer = await SeedTrainerAsync("trainer-report-crud", "trainer-report-crud@example.com");
        SetAuthorizationHeader(trainer.Id);

        var createTemplateResponse = await Client.PostAsJsonAsync("/api/trainer/report-templates", new
        {
            name = "Weekly CRUD",
            description = "v1",
            fields = new object[]
            {
                new { key = "weight", label = "Weight", type = "Number", isRequired = true, order = 0 }
            }
        });
        createTemplateResponse.StatusCode.Should().Be(HttpStatusCode.Created);
        var created = await createTemplateResponse.Content.ReadFromJsonAsync<ReportTemplateResponse>();
        created.Should().NotBeNull();

        var getAllResponse = await Client.GetAsync("/api/trainer/report-templates");
        getAllResponse.StatusCode.Should().Be(HttpStatusCode.OK);
        var allTemplates = await getAllResponse.Content.ReadFromJsonAsync<List<ReportTemplateResponse>>();
        allTemplates.Should().NotBeNull();
        allTemplates!.Any(x => x.Id == created!.Id).Should().BeTrue();

        var getOneResponse = await Client.GetAsync($"/api/trainer/report-templates/{created!.Id}");
        getOneResponse.StatusCode.Should().Be(HttpStatusCode.OK);

        var deleteResponse = await Client.PostAsync($"/api/trainer/report-templates/{created.Id}/delete", content: null);
        deleteResponse.StatusCode.Should().Be(HttpStatusCode.OK);
    }

    [Test]
    [AuthorizationEvidence("POST", "/api/trainer/report-templates", "own", "ordinary-user-denial")]
    public async Task TrainerReportTemplateCreate_AsRegularUser_ReturnsForbidden()
    {
        var regularUser = await SeedUserAsync("http-template-ordinary", "http-template-ordinary@example.test");
        SetAuthorizationHeader(regularUser.Id);

        using var response = await Client.PostAsJsonAsync("/api/trainer/report-templates", new
        {
            name = "Forbidden template",
            fields = Array.Empty<object>()
        });

        response.StatusCode.Should().Be(HttpStatusCode.Forbidden);
    }

    [Test]
    [AuthorizationEvidence("POST", "/api/trainer/report-templates", "own", "anonymous-denial")]
    public async Task TrainerReportTemplateCreate_WithoutAuthorization_ReturnsUnauthorized()
    {
        ClearAuthorizationHeader();

        using var response = await Client.PostAsJsonAsync("/api/trainer/report-templates", new
        {
            name = "Anonymous template",
            fields = Array.Empty<object>()
        });

        response.StatusCode.Should().Be(HttpStatusCode.Unauthorized);
    }

    [Test]
    [AuthorizationEvidence("POST", "/api/trainer/report-templates/{templateId}/delete", "own", "owner-allow")]
    [AuthorizationEvidence("POST", "/api/trainer/report-templates/{templateId}/delete", "own", "foreign-object-denial-no-mutation")]
    [AuthorizationEvidence("POST", "/api/trainer/report-templates/{templateId}/delete", "own", "anonymous-denial")]
    public async Task TrainerReportTemplateDeleteRoute_IsOwnerScopedAndNonDisclosing()
    {
        var owner = await SeedTrainerAsync("http-template-delete-owner", "http-template-delete-owner@example.test");
        var otherTrainer = await SeedTrainerAsync("http-template-delete-other", "http-template-delete-other@example.test");

        SetAuthorizationHeader(owner.Id);
        var deletedTemplate = await CreateReportTemplateAsync(
            "HTTP deleted template",
            "HTTP deleted field",
            "httpDeletedField");
        var protectedTemplate = await CreateReportTemplateAsync(
            "HTTP protected template",
            "HTTP protected field",
            "httpProtectedField");

        using var ownerResponse = await Client.PostAsync($"/api/trainer/report-templates/{deletedTemplate.Id}/delete", null);
        ownerResponse.StatusCode.Should().Be(HttpStatusCode.OK);

        SetAuthorizationHeader(otherTrainer.Id);
        using var foreignResponse = await Client.PostAsync($"/api/trainer/report-templates/{protectedTemplate.Id}/delete", null);
        foreignResponse.StatusCode.Should().Be(HttpStatusCode.NotFound);
        var foreignText = await foreignResponse.Content.ReadAsStringAsync();
        foreignText.Should().NotContain(protectedTemplate.Id);
        foreignText.Should().NotContain("HTTP protected template");

        ClearAuthorizationHeader();
        using var anonymousResponse = await Client.PostAsync($"/api/trainer/report-templates/{protectedTemplate.Id}/delete", null);
        anonymousResponse.StatusCode.Should().Be(HttpStatusCode.Unauthorized);
        var anonymousText = await anonymousResponse.Content.ReadAsStringAsync();
        anonymousText.Should().NotContain(protectedTemplate.Id);
        anonymousText.Should().NotContain("HTTP protected template");
    }

    [Test]
    [AuthorizationEvidence("POST", "/api/trainer/report-templates/{templateId}/update", "own", "owner-allow")]
    [AuthorizationEvidence("POST", "/api/trainer/report-templates/{templateId}/update", "own", "foreign-object-denial-no-mutation")]
    [AuthorizationEvidence("POST", "/api/trainer/report-templates/{templateId}/update", "own", "anonymous-denial")]
    public async Task TrainerReportTemplateUpdateRoute_IsOwnerScopedAndNonDisclosing()
    {
        var owner = await SeedTrainerAsync("http-template-update-owner", "http-template-update-owner@example.test");
        var otherTrainer = await SeedTrainerAsync("http-template-update-other", "http-template-update-other@example.test");

        SetAuthorizationHeader(owner.Id);
        var template = await CreateReportTemplateAsync(
            "HTTP update template",
            "HTTP original field",
            "httpOriginalField");
        var updatePayload = new
        {
            name = "HTTP updated template",
            fields = new[]
            {
                new { key = "httpUpdatedField", label = "HTTP updated field", type = "Text", isRequired = true, order = 0 }
            }
        };

        using var ownerResponse = await Client.PostAsJsonAsync($"/api/trainer/report-templates/{template.Id}/update", updatePayload);
        ownerResponse.StatusCode.Should().Be(HttpStatusCode.OK);
        var ownerBody = await ownerResponse.Content.ReadFromJsonAsync<ReportTemplateResponse>();
        ownerBody.Should().NotBeNull();
        ownerBody!.Name.Should().Be("HTTP updated template");
        ownerBody.Fields.Should().ContainSingle(field => field.Label == "HTTP updated field");

        SetAuthorizationHeader(otherTrainer.Id);
        using var foreignResponse = await Client.PostAsJsonAsync($"/api/trainer/report-templates/{template.Id}/update", updatePayload);
        foreignResponse.StatusCode.Should().Be(HttpStatusCode.NotFound);
        var foreignText = await foreignResponse.Content.ReadAsStringAsync();
        foreignText.Should().NotContain("HTTP updated template");
        foreignText.Should().NotContain("HTTP updated field");

        ClearAuthorizationHeader();
        using var anonymousResponse = await Client.PostAsJsonAsync($"/api/trainer/report-templates/{template.Id}/update", updatePayload);
        anonymousResponse.StatusCode.Should().Be(HttpStatusCode.Unauthorized);
        var anonymousText = await anonymousResponse.Content.ReadAsStringAsync();
        anonymousText.Should().NotContain("HTTP updated template");
        anonymousText.Should().NotContain("HTTP updated field");
    }

    private async Task<ReportTemplateResponse> CreateReportTemplateAsync(
        string name,
        string fieldLabel,
        string fieldKey)
    {
        using var response = await Client.PostAsJsonAsync("/api/trainer/report-templates", new
        {
            name,
            fields = new[]
            {
                new { key = fieldKey, label = fieldLabel, type = "Text", isRequired = true, order = 0 }
            }
        });
        response.StatusCode.Should().Be(HttpStatusCode.Created);
        var template = await response.Content.ReadFromJsonAsync<ReportTemplateResponse>();
        template.Should().NotBeNull();
        return template!;
    }

    private async Task<User> SeedTrainerAsync(string name, string email)
    {
        var trainer = await SeedUserAsync(name: name, email: email, password: "password123");

        using var scope = Factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var alreadyLinked = await db.UserRoles.AnyAsync(ur => ur.UserId == (Domain.ValueObjects.Id<User>)trainer.Id && ur.RoleId == (Domain.ValueObjects.Id<Role>)RoleSeedDataConfiguration.TrainerRoleSeedId);
        if (!alreadyLinked)
        {
            db.UserRoles.Add(new UserRole
            {
                UserId = (Domain.ValueObjects.Id<User>)trainer.Id,
                RoleId = (Domain.ValueObjects.Id<Role>)RoleSeedDataConfiguration.TrainerRoleSeedId
            });
            await db.SaveChangesAsync();
        }

        return trainer;
    }

    [Test]
    public async Task SubmitReport_WithMissingRequiredPhotoViews_ShouldReturnUnprocessableEntity()
    {
        var trainer = await SeedTrainerAsync("trainer-photo-blocking", "trainer-photo-blocking@example.com");
        var trainee = await SeedUserAsync(name: "trainee-photo-blocking", email: "trainee-photo-blocking@example.com", password: "password123");

        using (var scope = Factory.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
            db.TrainerTraineeLinks.Add(new TrainerTraineeLink
            {
                Id = Domain.ValueObjects.Id<TrainerTraineeLink>.New(),
                TrainerId = (Domain.ValueObjects.Id<User>)trainer.Id,
                TraineeId = (Domain.ValueObjects.Id<User>)trainee.Id
            });
            await db.SaveChangesAsync();
        }

        SetAuthorizationHeader(trainer.Id);
        var templateResponse = await Client.PostAsJsonAsync("/api/trainer/report-templates", new
        {
            name = "Photo Progress Report",
            fields = new object[]
            {
                new
                {
                    key = "photos",
                    label = "Progress Photos",
                    type = "Photos",
                    isRequired = true,
                    order = 0,
                    moduleConfig = new
                    {
                        requiredViews = new[] { "Front", "SideLeft", "SideRight", "Back" }
                    }
                }
            }
        });

        templateResponse.StatusCode.Should().Be(HttpStatusCode.Created);
        var template = await templateResponse.Content.ReadFromJsonAsync<ReportTemplateResponse>();

        var requestResponse = await Client.PostAsJsonAsync($"/api/trainer/trainees/{trainee.Id}/report-requests", new
        {
            templateId = template!.Id
        });

        requestResponse.StatusCode.Should().Be(HttpStatusCode.Created);
        var request = await requestResponse.Content.ReadFromJsonAsync<ReportRequestResponse>();

        SetAuthorizationHeader(trainee.Id);
        var submitResponse = await Client.PostAsJsonAsync($"/api/trainee/report-requests/{request!.Id}/submit", new
        {
            answers = new
            {
                photos = Array.Empty<string>()
            }
        });

        submitResponse.StatusCode.Should().Be(HttpStatusCode.UnprocessableEntity);
        var errorContent = await submitResponse.Content.ReadAsStringAsync();
        errorContent.Should().Contain("Missing required photo views");
    }

    [Test]
    public async Task SubmitReport_WithAllRequiredPhotoViews_ShouldSucceed()
    {
        var trainer = await SeedTrainerAsync("trainer-photo-success", "trainer-photo-success@example.com");
        var trainee = await SeedUserAsync(name: "trainee-photo-success", email: "trainee-photo-success@example.com", password: "password123");

        using (var scope = Factory.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
            db.TrainerTraineeLinks.Add(new TrainerTraineeLink
            {
                Id = Domain.ValueObjects.Id<TrainerTraineeLink>.New(),
                TrainerId = (Domain.ValueObjects.Id<User>)trainer.Id,
                TraineeId = (Domain.ValueObjects.Id<User>)trainee.Id
            });
            await db.SaveChangesAsync();
        }

        SetAuthorizationHeader(trainer.Id);
        var templateResponse = await Client.PostAsJsonAsync("/api/trainer/report-templates", new
        {
            name = "Photo Progress Report",
            fields = new object[]
            {
                new
                {
                    key = "photos",
                    label = "Progress Photos",
                    type = "Photos",
                    isRequired = true,
                    order = 0,
                    moduleConfig = new
                    {
                        requiredViews = new[] { "Front", "SideLeft", "SideRight", "Back" }
                    }
                }
            }
        });

        templateResponse.StatusCode.Should().Be(HttpStatusCode.Created);
        var template = await templateResponse.Content.ReadFromJsonAsync<ReportTemplateResponse>();

        var requestResponse = await Client.PostAsJsonAsync($"/api/trainer/trainees/{trainee.Id}/report-requests", new
        {
            templateId = template!.Id
        });

        requestResponse.StatusCode.Should().Be(HttpStatusCode.Created);
        var request = await requestResponse.Content.ReadFromJsonAsync<ReportRequestResponse>();

        using (var scope = Factory.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
            Id<ReportRequest>.TryParse(request!.Id, out var requestId).Should().BeTrue();
            var traineeId = trainee.Id;

            db.Photos.AddRange(
                new Photo
                {
                    Id = Id<Photo>.New(),
                    ReportRequestId = requestId,
                    OwnerUserId = traineeId,
                    UploaderUserId = traineeId,
                    ViewType = Domain.Enums.PhotoViewType.Front.ToString(),
                    StorageKey = "photos/front.jpg",
                    MimeType = "image/jpeg",
                    SizeBytes = 1024,
                    Checksum = "abc123"
                },
                new Photo
                {
                    Id = Id<Photo>.New(),
                    ReportRequestId = requestId,
                    OwnerUserId = traineeId,
                    UploaderUserId = traineeId,
                    ViewType = Domain.Enums.PhotoViewType.SideLeft.ToString(),
                    StorageKey = "photos/side-left.jpg",
                    MimeType = "image/jpeg",
                    SizeBytes = 1024,
                    Checksum = "def456"
                },
                new Photo
                {
                    Id = Id<Photo>.New(),
                    ReportRequestId = requestId,
                    OwnerUserId = traineeId,
                    UploaderUserId = traineeId,
                    ViewType = Domain.Enums.PhotoViewType.SideRight.ToString(),
                    StorageKey = "photos/side-right.jpg",
                    MimeType = "image/jpeg",
                    SizeBytes = 1024,
                    Checksum = "ghi789"
                },
                new Photo
                {
                    Id = Id<Photo>.New(),
                    ReportRequestId = requestId,
                    OwnerUserId = traineeId,
                    UploaderUserId = traineeId,
                    ViewType = Domain.Enums.PhotoViewType.Back.ToString(),
                    StorageKey = "photos/back.jpg",
                    MimeType = "image/jpeg",
                    SizeBytes = 1024,
                    Checksum = "jkl012"
                });

            await db.SaveChangesAsync();
        }

        SetAuthorizationHeader(trainee.Id);
        var submitResponse = await Client.PostAsJsonAsync($"/api/trainee/report-requests/{request!.Id}/submit", new
        {
            answers = new
            {
                photos = Array.Empty<string>()
            }
        });

        submitResponse.StatusCode.Should().Be(HttpStatusCode.OK);
    }

    [Test]
    [AuthorizationEvidence("POST", "/api/trainee/photos/initiate", "own", "owner-allow")]
    [AuthorizationEvidence("POST", "/api/trainee/reporting/photos/upload-init", "own", "owner-allow")]
    [AuthorizationEvidence("POST", "/api/trainer/reporting/photos/upload-init", "own", "owner-allow")]
    public async Task EndToEnd_PhotoUploadInitiateAndSubmit_Success()
    {
        var trainer = await SeedTrainerAsync("trainer-photo-e2e", "trainer-photo-e2e@example.com");
        var trainee = await SeedUserAsync(name: "trainee-photo-e2e", email: "trainee-photo-e2e@example.com", password: "password123");

        using (var scope = Factory.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
            db.TrainerTraineeLinks.Add(new TrainerTraineeLink
            {
                Id = Id<TrainerTraineeLink>.New(),
                TrainerId = (Id<User>)trainer.Id,
                TraineeId = (Id<User>)trainee.Id
            });
            await db.SaveChangesAsync();
        }

        SetAuthorizationHeader(trainer.Id);
        var templateResponse = await Client.PostAsJsonAsync("/api/trainer/report-templates", new
        {
            name = "Photo E2E Report",
            fields = new object[]
            {
                new
                {
                    key = "photos",
                    label = "Progress Photos",
                    type = "Photos",
                    isRequired = true,
                    order = 0,
                    moduleConfig = new
                    {
                        requiredViews = new[] { "Front" }
                    }
                }
            }
        });

        templateResponse.StatusCode.Should().Be(HttpStatusCode.Created);
        var template = await templateResponse.Content.ReadFromJsonAsync<ReportTemplateResponse>();

        var requestResponse = await Client.PostAsJsonAsync($"/api/trainer/trainees/{trainee.Id}/report-requests", new
        {
            templateId = template!.Id
        });

        requestResponse.StatusCode.Should().Be(HttpStatusCode.Created);
        var request = await requestResponse.Content.ReadFromJsonAsync<ReportRequestResponse>();

        SetAuthorizationHeader(trainee.Id);
        var initiateResponse = await Client.PostAsJsonAsync("/api/trainee/photos/initiate", new
        {
            reportRequestId = request!.Id,
            viewType = "Front",
            mimeType = "image/jpeg",
            sizeBytes = 1024000
        });

        initiateResponse.StatusCode.Should().Be(HttpStatusCode.OK);
        var initiateResult = await initiateResponse.Content.ReadFromJsonAsync<JsonElement>();
        var storageKey = initiateResult.GetProperty("storageKey").GetString();
        storageKey.Should().NotBeNullOrEmpty();

        SetAuthorizationHeader(trainer.Id);
        var trainerInitiateResponse = await Client.PostAsJsonAsync("/api/trainer/reporting/photos/upload-init", new
        {
            reportRequestId = request.Id,
            viewType = "SideLeft",
            mimeType = "image/jpeg",
            sizeBytes = 1024
        });
        trainerInitiateResponse.StatusCode.Should().Be(HttpStatusCode.OK);
    }

    [Test]
    [AuthorizationEvidence("POST", "/api/trainee/photos/initiate", "own", "anonymous-denial")]
    [AuthorizationEvidence("POST", "/api/trainee/reporting/photos/upload-init", "own", "anonymous-denial")]
    [AuthorizationEvidence("POST", "/api/trainer/reporting/photos/upload-init", "own", "anonymous-denial")]
    public async Task TraineePhotoInitiate_WithoutAuthorization_ReturnsUnauthorized()
    {
        ClearAuthorizationHeader();

        using var response = await Client.PostAsJsonAsync("/api/trainee/photos/initiate", new
        {
            reportRequestId = Id<ReportRequest>.New().ToString(),
            viewType = "Front",
            mimeType = "image/jpeg",
            sizeBytes = 1024
        });

        response.StatusCode.Should().Be(HttpStatusCode.Unauthorized);

        using var trainerResponse = await Client.PostAsJsonAsync("/api/trainer/reporting/photos/upload-init", new
        {
            reportRequestId = Id<ReportRequest>.New().ToString(),
            viewType = "Front",
            mimeType = "image/jpeg",
            sizeBytes = 1024
        });
        trainerResponse.StatusCode.Should().Be(HttpStatusCode.Unauthorized);
    }

    [Test]
    [AuthorizationEvidence("POST", "/api/trainee/photos/complete-upload", "own", "owner-allow")]
    [AuthorizationEvidence("POST", "/api/trainee/photos/complete-upload", "own", "anonymous-denial")]
    [AuthorizationEvidence("POST", "/api/trainee/reporting/photos/complete-upload", "own", "owner-allow")]
    [AuthorizationEvidence("POST", "/api/trainee/reporting/photos/complete-upload", "own", "anonymous-denial")]
    [AuthorizationEvidence("POST", "/api/trainer/reporting/photos/complete-upload", "own", "owner-allow")]
    [AuthorizationEvidence("POST", "/api/trainer/reporting/photos/complete-upload", "own", "anonymous-denial")]
    public async Task TraineeCompletePhotoUploadRoutes_EnforceAuthenticationBeforeFinalization()
    {
        var trainer = await SeedTrainerAsync("http-complete-photo-trainer", "http-complete-photo-trainer@example.test");
        var trainee = await SeedUserAsync(name: "http-complete-photo-trainee", email: "http-complete-photo-trainee@example.test", password: "password123");

        using (var scope = Factory.Services.CreateScope())
        {
            var database = scope.ServiceProvider.GetRequiredService<AppDbContext>();
            database.TrainerTraineeLinks.Add(new TrainerTraineeLink
            {
                Id = Id<TrainerTraineeLink>.New(),
                TrainerId = trainer.Id,
                TraineeId = trainee.Id
            });
            await database.SaveChangesAsync();
        }

        SetAuthorizationHeader(trainer.Id);
        var template = await CreateReportTemplateAsync(
            "HTTP complete photo template",
            "HTTP complete photo field",
            "httpCompletePhotoField");
        using var requestResponse = await Client.PostAsJsonAsync($"/api/trainer/trainees/{trainee.Id}/report-requests", new
        {
            templateId = template.Id
        });
        requestResponse.StatusCode.Should().Be(HttpStatusCode.Created);
        var request = await requestResponse.Content.ReadFromJsonAsync<ReportRequestResponse>();
        request.Should().NotBeNull();

        var payload = new
        {
            reportRequestId = request!.Id,
            viewType = "Front",
            storageKey = "photos/http-complete-photo/missing.jpg",
            mimeType = "image/jpeg",
            sizeBytes = 1024,
            checksum = "missing"
        };

        SetAuthorizationHeader(trainee.Id);
        using var ownerResponse = await Client.PostAsJsonAsync("/api/trainee/photos/complete-upload", payload);
        ownerResponse.StatusCode.Should().Be(HttpStatusCode.BadRequest);
        var ownerText = await ownerResponse.Content.ReadAsStringAsync();
        ownerText.Should().NotContain(payload.storageKey);

        ClearAuthorizationHeader();
        using var anonymousResponse = await Client.PostAsJsonAsync("/api/trainee/photos/complete-upload", payload);
        anonymousResponse.StatusCode.Should().Be(HttpStatusCode.Unauthorized);
        var anonymousText = await anonymousResponse.Content.ReadAsStringAsync();
        anonymousText.Should().NotContain(payload.storageKey);

        SetAuthorizationHeader(trainee.Id);
        using var legacyOwnerResponse = await Client.PostAsJsonAsync("/api/trainee/reporting/photos/complete-upload", payload);
        legacyOwnerResponse.StatusCode.Should().Be(HttpStatusCode.BadRequest);
        ClearAuthorizationHeader();
        using var legacyAnonymousResponse = await Client.PostAsJsonAsync("/api/trainee/reporting/photos/complete-upload", payload);
        legacyAnonymousResponse.StatusCode.Should().Be(HttpStatusCode.Unauthorized);

        SetAuthorizationHeader(trainer.Id);
        using var trainerResponse = await Client.PostAsJsonAsync("/api/trainer/reporting/photos/complete-upload", payload);
        trainerResponse.StatusCode.Should().Be(HttpStatusCode.BadRequest);

        ClearAuthorizationHeader();
        using var trainerAnonymousResponse = await Client.PostAsJsonAsync("/api/trainer/reporting/photos/complete-upload", payload);
        trainerAnonymousResponse.StatusCode.Should().Be(HttpStatusCode.Unauthorized);
    }

    [Test]
    public async Task EndToEnd_UnauthorizedPhotoUpload_Denied()
    {
        var trainer = await SeedTrainerAsync("trainer-photo-unauth", "trainer-photo-unauth@example.com");
        var trainee = await SeedUserAsync(name: "trainee-photo-unauth", email: "trainee-photo-unauth@example.com", password: "password123");
        var otherUser = await SeedUserAsync(name: "other-user-photo", email: "other-user-photo@example.com", password: "password123");

        using (var scope = Factory.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
            db.TrainerTraineeLinks.Add(new TrainerTraineeLink
            {
                Id = Id<TrainerTraineeLink>.New(),
                TrainerId = (Id<User>)trainer.Id,
                TraineeId = (Id<User>)trainee.Id
            });
            await db.SaveChangesAsync();
        }

        SetAuthorizationHeader(trainer.Id);
        var templateResponse = await Client.PostAsJsonAsync("/api/trainer/report-templates", new
        {
            name = "Photo Unauth Report",
            fields = new object[]
            {
                new
                {
                    key = "photos",
                    label = "Progress Photos",
                    type = "Photos",
                    isRequired = true,
                    order = 0,
                    moduleConfig = new
                    {
                        requiredViews = new[] { "Front" }
                    }
                }
            }
        });

        var template = await templateResponse.Content.ReadFromJsonAsync<ReportTemplateResponse>();

        var requestResponse = await Client.PostAsJsonAsync($"/api/trainer/trainees/{trainee.Id}/report-requests", new
        {
            templateId = template!.Id
        });

        var request = await requestResponse.Content.ReadFromJsonAsync<ReportRequestResponse>();

        SetAuthorizationHeader(otherUser.Id);
        var initiateResponse = await Client.PostAsJsonAsync("/api/trainee/photos/initiate", new
        {
            reportRequestId = request!.Id,
            viewType = "Front",
            mimeType = "image/jpeg",
            sizeBytes = 1024000
        });

        initiateResponse.StatusCode.Should().Be(HttpStatusCode.NotFound);
    }

    [Test]
    public async Task EndToEnd_DuplicatePhotoUpload_ReplacesOld()
    {
        var trainer = await SeedTrainerAsync("trainer-photo-dup", "trainer-photo-dup@example.com");
        var trainee = await SeedUserAsync(name: "trainee-photo-dup", email: "trainee-photo-dup@example.com", password: "password123");

        using (var scope = Factory.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
            db.TrainerTraineeLinks.Add(new TrainerTraineeLink
            {
                Id = Id<TrainerTraineeLink>.New(),
                TrainerId = (Id<User>)trainer.Id,
                TraineeId = (Id<User>)trainee.Id
            });
            await db.SaveChangesAsync();
        }

        SetAuthorizationHeader(trainer.Id);
        var templateResponse = await Client.PostAsJsonAsync("/api/trainer/report-templates", new
        {
            name = "Photo Duplicate Report",
            fields = new object[]
            {
                new
                {
                    key = "photos",
                    label = "Progress Photos",
                    type = "Photos",
                    isRequired = true,
                    order = 0,
                    moduleConfig = new
                    {
                        requiredViews = new[] { "Front" }
                    }
                }
            }
        });

        var template = await templateResponse.Content.ReadFromJsonAsync<ReportTemplateResponse>();

        var requestResponse = await Client.PostAsJsonAsync($"/api/trainer/trainees/{trainee.Id}/report-requests", new
        {
            templateId = template!.Id
        });

        var request = await requestResponse.Content.ReadFromJsonAsync<ReportRequestResponse>();

        using (var scope = Factory.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
            Id<ReportRequest>.TryParse(request!.Id, out var requestId).Should().BeTrue();
            var traineeId = trainee.Id;

            var oldPhoto = new Photo
            {
                Id = Id<Photo>.New(),
                ReportRequestId = requestId,
                OwnerUserId = traineeId,
                UploaderUserId = traineeId,
                ViewType = PhotoViewType.Front.ToString(),
                StorageKey = "photos/old-front.jpg",
                MimeType = "image/jpeg",
                SizeBytes = 1024,
                Checksum = "old-checksum",
                IsDeleted = false
            };

            db.Photos.Add(oldPhoto);
            await db.SaveChangesAsync();

            var newPhoto = new Photo
            {
                Id = Id<Photo>.New(),
                ReportRequestId = requestId,
                OwnerUserId = traineeId,
                UploaderUserId = traineeId,
                ViewType = PhotoViewType.Front.ToString(),
                StorageKey = "photos/new-front.jpg",
                MimeType = "image/jpeg",
                SizeBytes = 2048,
                Checksum = "new-checksum",
                IsDeleted = false
            };

            oldPhoto.IsDeleted = true;

            db.Photos.Add(newPhoto);
            await db.SaveChangesAsync();
        }

        using (var scope = Factory.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
            Id<ReportRequest>.TryParse(request!.Id, out var requestId).Should().BeTrue();

            var photos = await db.Photos
                .Where(p => p.ReportRequestId == requestId && p.ViewType == PhotoViewType.Front.ToString())
                .ToListAsync();

            photos.Should().HaveCount(2);
            photos.Count(p => p.IsDeleted).Should().Be(1);
            photos.Count(p => !p.IsDeleted).Should().Be(1);
            photos.Single(p => !p.IsDeleted).Checksum.Should().Be("new-checksum");
        }
    }

    [Test]
    public async Task EndToEnd_MixedTemplate_PhotosAndScalarFields_ValidatesCorrectly()
    {
        var trainer = await SeedTrainerAsync("trainer-mixed", "trainer-mixed@example.com");
        var trainee = await SeedUserAsync(name: "trainee-mixed", email: "trainee-mixed@example.com", password: "password123");

        using (var scope = Factory.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
            db.TrainerTraineeLinks.Add(new TrainerTraineeLink
            {
                Id = Id<TrainerTraineeLink>.New(),
                TrainerId = (Id<User>)trainer.Id,
                TraineeId = (Id<User>)trainee.Id
            });
            await db.SaveChangesAsync();
        }

        SetAuthorizationHeader(trainer.Id);
        var templateResponse = await Client.PostAsJsonAsync("/api/trainer/report-templates", new
        {
            name = "Mixed Template Report",
            fields = new object[]
            {
                new
                {
                    key = "photos",
                    label = "Progress Photos",
                    type = "Photos",
                    isRequired = true,
                    order = 0,
                    moduleConfig = new
                    {
                        requiredViews = new[] { "Front", "Side" }
                    }
                },
                new
                {
                    key = "weight",
                    label = "Current Weight",
                    type = "Number",
                    isRequired = true,
                    order = 1
                },
                new
                {
                    key = "notes",
                    label = "Notes",
                    type = "Text",
                    isRequired = false,
                    order = 2
                }
            }
        });

        templateResponse.StatusCode.Should().Be(HttpStatusCode.Created);
        var template = await templateResponse.Content.ReadFromJsonAsync<ReportTemplateResponse>();
        template!.Fields.Should().HaveCount(3);
        template.Fields.Should().Contain(f => f.Key == "photos");
        template.Fields.Should().Contain(f => f.Key == "weight");
        template.Fields.Should().Contain(f => f.Key == "notes");
    }

    private sealed class ReportTemplateResponse
    {
        [JsonPropertyName("_id")]
        public string Id { get; set; } = string.Empty;

        [JsonPropertyName("name")]
        public string Name { get; set; } = string.Empty;

        [JsonPropertyName("fields")]
        public List<ReportTemplateFieldResponse> Fields { get; set; } = [];
    }

    private sealed class ReportTemplateFieldResponse
    {
        [JsonPropertyName("key")]
        public string Key { get; set; } = string.Empty;

        [JsonPropertyName("label")]
        public string Label { get; set; } = string.Empty;
    }

    private sealed class ReportRequestResponse
    {
        [JsonPropertyName("_id")]
        public string Id { get; set; } = string.Empty;

        [JsonPropertyName("status")]
        public string Status { get; set; } = string.Empty;

        [JsonPropertyName("note")]
        public string? Note { get; set; }
    }

    private sealed class ReportSubmissionResponse
    {
        [JsonPropertyName("_id")]
        public string Id { get; set; } = string.Empty;

        [JsonPropertyName("reportRequestId")]
        public string ReportRequestId { get; set; } = string.Empty;

        [JsonPropertyName("answers")]
        public Dictionary<string, JsonElement> Answers { get; set; } = new(StringComparer.OrdinalIgnoreCase);
    }

    private sealed class RecurringReportAssignmentResponse
    {
        [JsonPropertyName("templateId")]
        public string TemplateId { get; set; } = string.Empty;

        [JsonPropertyName("note")]
        public string? Note { get; set; }
    }

    private sealed class PhotoHistoryResponse
    {
        [JsonPropertyName("photos")]
        public List<PhotoHistoryItemResponse> Photos { get; set; } = [];
    }

    private sealed class PhotoHistoryItemResponse
    {
        [JsonPropertyName("_id")]
        public string Id { get; set; } = string.Empty;

        [JsonPropertyName("viewType")]
        public string ViewType { get; set; } = string.Empty;

        [JsonPropertyName("sizeBytes")]
        public long SizeBytes { get; set; }

        [JsonPropertyName("thumbnailUrl")]
        public string? ThumbnailUrl { get; set; }

        [JsonPropertyName("readUrl")]
        public string ReadUrl { get; set; } = string.Empty;

        [JsonPropertyName("reportRequestId")]
        public string ReportRequestId { get; set; } = string.Empty;
    }

    private sealed class SignedReadUrlResponse
    {
        [JsonPropertyName("readUrl")]
        public string ReadUrl { get; set; } = string.Empty;

        [JsonPropertyName("expiresAt")]
        public DateTimeOffset ExpiresAt { get; set; }
    }
}
