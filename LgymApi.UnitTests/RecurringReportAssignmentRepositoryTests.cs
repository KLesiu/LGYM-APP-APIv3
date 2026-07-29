using FluentAssertions;
using LgymApi.Domain.Entities;
using LgymApi.Domain.Enums;
using LgymApi.Domain.ValueObjects;
using LgymApi.Identity.Contracts;
using LgymApi.Infrastructure.Data;
using LgymApi.Infrastructure.Repositories.Reporting;
using Microsoft.EntityFrameworkCore;

namespace LgymApi.UnitTests;

[TestFixture]
public sealed class RecurringReportAssignmentRepositoryTests
{
    [Test]
    public async Task FindByCurrentRequest_ReturnsAssignmentWithOrderedTemplateFields()
    {
        await using var db = CreateDbContext();
        var trainer = CreateUser("trainer@example.com");
        var trainee = CreateUser("trainee@example.com");
        var template = CreateTemplate(trainer.Id);
        var request = CreateRequest(trainer.Id, trainee.Id, template);
        var assignment = CreateAssignment(trainer.Id, trainee.Id, template, request);
        db.AddRange(trainer, trainee, template, request, assignment);
        await db.SaveChangesAsync();
        var persistence = new RecurringReportAssignmentPersistenceRepository(db);

        var result = await persistence.FindByCurrentRequestAsync(request.Id);

        result.Should().NotBeNull();
        result!.Template.Fields.Select(field => field.Order).Should().BeInAscendingOrder();
        result.CurrentReportRequest!.Template.Fields.Select(field => field.Order).Should().BeInAscendingOrder();
    }

    [Test]
    public async Task ListDue_FiltersDateWindowAndOrdersByEligibility()
    {
        await using var db = CreateDbContext();
        var now = DateTimeOffset.UtcNow;
        var trainer = CreateUser("trainer@example.com");
        var trainee = CreateUser("trainee@example.com");
        var template = CreateTemplate(trainer.Id);
        db.AddRange(
            trainer,
            trainee,
            template,
            CreateAssignment(trainer.Id, trainee.Id, template, startsAt: now.AddDays(-10), note: "open"),
            CreateAssignment(trainer.Id, trainee.Id, template, startsAt: now.AddDays(-20), endsAt: now.AddDays(5), note: "ending"),
            CreateAssignment(trainer.Id, trainee.Id, template, startsAt: now.AddDays(-30), endsAt: now.AddDays(-5), note: "ended"),
            CreateAssignment(trainer.Id, trainee.Id, template, startsAt: now.AddDays(5), note: "future"));
        await db.SaveChangesAsync();
        var persistence = new RecurringReportAssignmentPersistenceRepository(db);

        var result = await persistence.ListDueAsync(now);

        result.Select(assignment => assignment.Note).Should().BeEquivalentTo(["ending", "open"]);
        result.Should().OnlyContain(assignment => assignment.Template.Fields.Count == 2);
    }

    private static AppDbContext CreateDbContext()
        => new(new DbContextOptionsBuilder<AppDbContext>()
            .UseInMemoryDatabase($"recurring-{Id<RecurringReportAssignmentRepositoryTests>.New():N}")
            .Options);

    private static User CreateUser(string email)
        => new() { Id = Id<User>.New(), Name = email, Email = email, ProfileRank = "Rookie" };

    private static ReportTemplate CreateTemplate(Id<User> trainerId)
    {
        var templateId = Id<ReportTemplate>.New();
        return new ReportTemplate
        {
            Id = templateId,
            TrainerId = trainerId,
            Name = "Weekly",
            Fields =
            [
                new ReportTemplateField { Id = Id<ReportTemplateField>.New(), TemplateId = templateId, Key = "b", Order = 2 },
                new ReportTemplateField { Id = Id<ReportTemplateField>.New(), TemplateId = templateId, Key = "a", Order = 1 }
            ]
        };
    }

    private static ReportRequest CreateRequest(Id<User> trainerId, Id<User> traineeId, ReportTemplate template)
        => new()
        {
            Id = Id<ReportRequest>.New(),
            TrainerId = trainerId,
            TraineeId = traineeId,
            TemplateId = template.Id,
            Template = template,
            Status = ReportRequestStatus.Pending
        };

    private static RecurringReportAssignment CreateAssignment(
        Id<User> trainerId,
        Id<User> traineeId,
        ReportTemplate template,
        ReportRequest? request = null,
        DateTimeOffset? startsAt = null,
        DateTimeOffset? endsAt = null,
        string note = "assignment")
        => new()
        {
            Id = Id<RecurringReportAssignment>.New(),
            TrainerId = trainerId,
            TraineeId = traineeId,
            TemplateId = template.Id,
            Template = template,
            IntervalValue = 1,
            IntervalUnit = RecurringReportIntervalUnit.Week,
            StartsAt = startsAt ?? DateTimeOffset.UtcNow.AddDays(-1),
            EndsAt = endsAt,
            IsActive = true,
            Note = note,
            NextEligibleAt = startsAt ?? DateTimeOffset.UtcNow,
            CurrentReportRequestId = request?.Id,
            CurrentReportRequest = request
        };
}
