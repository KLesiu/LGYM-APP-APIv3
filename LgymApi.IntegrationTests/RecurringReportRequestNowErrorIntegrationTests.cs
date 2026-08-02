using System.Net;
using System.Text.Json;
using FluentAssertions;
using LgymApi.Domain.Entities;
using LgymApi.Domain.Enums;
using LgymApi.Domain.ValueObjects;
using LgymApi.Infrastructure.Data;
using LgymApi.Infrastructure.Data.SeedData;
using LgymApi.Resources;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace LgymApi.IntegrationTests;

[TestFixture]
public sealed class RecurringReportRequestNowErrorIntegrationTests : IntegrationTestBase
{
    private static readonly string[] Cultures = ["en", "pl"];

    [Test]
    public async Task MalformedIds_ReturnLocalizedBadRequest()
    {
        var trainer = await SeedTrainerAsync();
        SetAuthorizationHeader(trainer.Id);

        await AssertLocalizedErrorAsync(
            $"/api/trainer/trainees/not-a-guid/recurring-report-assignments/{Id<RecurringReportAssignment>.New()}/request-now",
            HttpStatusCode.BadRequest,
            () => Messages.UserIdRequired);
        await AssertLocalizedErrorAsync(
            $"/api/trainer/trainees/{Id<User>.New()}/recurring-report-assignments/not-a-guid/request-now",
            HttpStatusCode.BadRequest,
            () => Messages.FieldRequired);
        await AssertNoCreationAsync();
    }

    [Test]
    public async Task ValidNonTrainer_ReturnsLocalizedForbidden()
    {
        var caller = await SeedUserAsync("request-now-non-trainer", "request-now-non-trainer@example.com");
        SetAuthorizationHeader(caller.Id);

        await AssertLocalizedErrorAsync(
            Route(Id<User>.New(), Id<RecurringReportAssignment>.New()),
            HttpStatusCode.Forbidden,
            () => Messages.Unauthorized);
        await AssertNoCreationAsync();
    }

    [Test]
    public async Task MissingAssignment_ReturnsLocalizedNotFound()
    {
        var scenario = await SeedScenarioAsync();
        SetAuthorizationHeader(scenario.TrainerId);

        await AssertLocalizedErrorAsync(
            Route(scenario.TraineeId, Id<RecurringReportAssignment>.New()),
            HttpStatusCode.NotFound,
            () => Messages.DidntFind);
        await AssertNoCreationAsync();
    }

    [Test]
    public async Task DeletedAssignment_ReturnsLocalizedNotFoundWithoutWrites()
    {
        var scenario = await SeedScenarioAsync();
        await MutateAssignmentAsync(scenario.AssignmentId, assignment => assignment.IsDeleted = true);
        SetAuthorizationHeader(scenario.TrainerId);

        await AssertLocalizedErrorAsync(
            Route(scenario.TraineeId, scenario.AssignmentId),
            HttpStatusCode.NotFound,
            () => Messages.DidntFind);
        await AssertNoCreationAsync();
    }

    [Test]
    public async Task ForeignAssignment_ReturnsLocalizedNotFoundWithoutWrites()
    {
        var scenario = await SeedScenarioAsync();
        var foreignTrainer = await SeedUserAsync("request-now-foreign", "request-now-foreign@example.com");
        await MutateAssignmentAsync(scenario.AssignmentId, assignment => assignment.TrainerId = foreignTrainer.Id);
        SetAuthorizationHeader(scenario.TrainerId);

        await AssertLocalizedErrorAsync(
            Route(scenario.TraineeId, scenario.AssignmentId),
            HttpStatusCode.NotFound,
            () => Messages.DidntFind);
        await AssertNoCreationAsync();
    }

    [Test]
    public async Task MissingRelationship_ReturnsLocalizedNotFoundWithoutWrites()
    {
        var scenario = await SeedScenarioAsync(linkRelationship: false);
        SetAuthorizationHeader(scenario.TrainerId);

        await AssertLocalizedErrorAsync(
            Route(scenario.TraineeId, scenario.AssignmentId),
            HttpStatusCode.NotFound,
            () => Messages.DidntFind);
        await AssertNoCreationAsync();
    }

    [Test]
    public async Task UnresolvedCurrentRequest_ReturnsLocalizedConflictWithoutReplacement()
    {
        var scenario = await SeedScenarioAsync();
        var currentRequestId = await AddPendingCurrentRequestAsync(scenario);
        SetAuthorizationHeader(scenario.TrainerId);

        await AssertLocalizedErrorAsync(
            Route(scenario.TraineeId, scenario.AssignmentId),
            HttpStatusCode.Conflict,
            () => Messages.RecurringReportRequestInProgress);

        using var scope = Factory.Services.CreateScope();
        var database = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        (await database.ReportRequests.CountAsync()).Should().Be(1);
        (await database.RecurringReportAssignments.AsNoTracking()
            .SingleAsync(candidate => candidate.Id == scenario.AssignmentId))
            .CurrentReportRequestId.Should().Be(currentRequestId);
        (await database.CommandEnvelopes.CountAsync()).Should().Be(0);
    }

    [TestCase(UnavailableState.Inactive)]
    [TestCase(UnavailableState.NotStarted)]
    [TestCase(UnavailableState.Ended)]
    public async Task InactiveWindow_ReturnsLocalizedConflictWithoutWrites(UnavailableState state)
    {
        var scenario = await SeedScenarioAsync();
        await MutateAssignmentAsync(scenario.AssignmentId, assignment =>
        {
            if (state == UnavailableState.Inactive)
            {
                assignment.IsActive = false;
            }
            else if (state == UnavailableState.NotStarted)
            {
                assignment.StartsAt = DateTimeOffset.UtcNow.AddDays(1);
            }
            else
            {
                assignment.EndsAt = DateTimeOffset.UtcNow.AddDays(-1);
            }
        });
        SetAuthorizationHeader(scenario.TrainerId);

        await AssertLocalizedErrorAsync(
            Route(scenario.TraineeId, scenario.AssignmentId),
            HttpStatusCode.Conflict,
            () => Messages.RecurringReportAssignmentUnavailable);
        await AssertNoCreationAsync();
    }

    [Test]
    public async Task DeletedTemplate_ReturnsLocalizedConflictAndDeactivatesWithoutCreating()
    {
        var scenario = await SeedScenarioAsync();
        using (var scope = Factory.Services.CreateScope())
        {
            var database = scope.ServiceProvider.GetRequiredService<AppDbContext>();
            var template = await database.ReportTemplates.SingleAsync(candidate => candidate.Id == scenario.TemplateId);
            template.IsDeleted = true;
            await database.SaveChangesAsync();
        }
        SetAuthorizationHeader(scenario.TrainerId);

        await AssertLocalizedErrorAsync(
            Route(scenario.TraineeId, scenario.AssignmentId),
            HttpStatusCode.Conflict,
            () => Messages.RecurringReportTemplateUnavailable);

        using var verificationScope = Factory.Services.CreateScope();
        var verificationDatabase = verificationScope.ServiceProvider.GetRequiredService<AppDbContext>();
        (await verificationDatabase.RecurringReportAssignments.IgnoreQueryFilters().AsNoTracking()
            .SingleAsync(candidate => candidate.Id == scenario.AssignmentId)).IsActive.Should().BeFalse();
        (await verificationDatabase.ReportRequests.CountAsync()).Should().Be(0);
        (await verificationDatabase.CommandEnvelopes.CountAsync()).Should().Be(0);
    }

    private async Task AssertLocalizedErrorAsync(
        string route,
        HttpStatusCode expectedStatus,
        Func<string> messageAccessor)
    {
        foreach (var culture in Cultures)
        {
            using var request = new HttpRequestMessage(HttpMethod.Post, route);
            request.Headers.AcceptLanguage.ParseAdd(culture);
            using var response = await Client.SendAsync(request);
            response.StatusCode.Should().Be(expectedStatus);
            using var body = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
            body.RootElement.EnumerateObject().Select(property => property.Name).Should().Equal("msg");
            body.RootElement.GetProperty("msg").GetString().Should().Be(
                CompatibilityResourceMessage.InCulture(culture, messageAccessor));
        }
    }

    private async Task<Scenario> SeedScenarioAsync(bool linkRelationship = true)
    {
        var trainer = await SeedTrainerAsync();
        var trainee = await SeedUserAsync("request-now-error-trainee", "request-now-error-trainee@example.com");
        if (linkRelationship)
        {
            await LinkTrainerAndTraineeAsync(trainer.Id, trainee.Id);
        }

        using var scope = Factory.Services.CreateScope();
        var database = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var timestamp = DateTimeOffset.UtcNow;
        var template = new ReportTemplate
        {
            Id = Id<ReportTemplate>.New(),
            TrainerId = trainer.Id,
            Name = "Request-now error template",
            CreatedAt = timestamp,
            UpdatedAt = timestamp
        };
        var assignment = new RecurringReportAssignment
        {
            Id = Id<RecurringReportAssignment>.New(),
            TrainerId = trainer.Id,
            TraineeId = trainee.Id,
            TemplateId = template.Id,
            IntervalValue = 1,
            IntervalUnit = RecurringReportIntervalUnit.Week,
            StartsAt = timestamp.AddDays(-1),
            EndsAt = timestamp.AddDays(30),
            IsActive = true,
            CreatedAt = timestamp,
            UpdatedAt = timestamp
        };
        database.ReportTemplates.Add(template);
        database.RecurringReportAssignments.Add(assignment);
        await database.SaveChangesAsync();
        return new Scenario(trainer.Id, trainee.Id, template.Id, assignment.Id);
    }

    private async Task<Id<ReportRequest>> AddPendingCurrentRequestAsync(Scenario scenario)
    {
        using var scope = Factory.Services.CreateScope();
        var database = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var timestamp = DateTimeOffset.UtcNow;
        var request = new ReportRequest
        {
            Id = Id<ReportRequest>.New(),
            TrainerId = scenario.TrainerId,
            TraineeId = scenario.TraineeId,
            TemplateId = scenario.TemplateId,
            RecurringReportAssignmentId = scenario.AssignmentId,
            Status = ReportRequestStatus.Pending,
            CreatedAt = timestamp,
            UpdatedAt = timestamp
        };
        database.ReportRequests.Add(request);
        await database.SaveChangesAsync();
        var assignment = await database.RecurringReportAssignments.SingleAsync(candidate => candidate.Id == scenario.AssignmentId);
        assignment.CurrentReportRequestId = request.Id;
        assignment.LastRequestCreatedAt = timestamp;
        await database.SaveChangesAsync();
        return request.Id;
    }

    private async Task MutateAssignmentAsync(
        Id<RecurringReportAssignment> assignmentId,
        Action<RecurringReportAssignment> mutate)
    {
        using var scope = Factory.Services.CreateScope();
        var database = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var assignment = await database.RecurringReportAssignments.SingleAsync(candidate => candidate.Id == assignmentId);
        mutate(assignment);
        await database.SaveChangesAsync();
    }

    private async Task AssertNoCreationAsync()
    {
        using var scope = Factory.Services.CreateScope();
        var database = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        (await database.ReportRequests.CountAsync()).Should().Be(0);
        (await database.CommandEnvelopes.CountAsync()).Should().Be(0);
    }

    private async Task<User> SeedTrainerAsync()
    {
        var trainer = await SeedUserAsync("request-now-error-trainer", "request-now-error-trainer@example.com");
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

    private static string Route(Id<User> traineeId, Id<RecurringReportAssignment> assignmentId) =>
        $"/api/trainer/trainees/{traineeId}/recurring-report-assignments/{assignmentId}/request-now";

    public enum UnavailableState
    {
        Inactive,
        NotStarted,
        Ended
    }

    private sealed record Scenario(
        Id<User> TrainerId,
        Id<User> TraineeId,
        Id<ReportTemplate> TemplateId,
        Id<RecurringReportAssignment> AssignmentId);
}
