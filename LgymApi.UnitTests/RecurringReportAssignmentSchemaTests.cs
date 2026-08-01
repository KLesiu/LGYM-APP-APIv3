using FluentAssertions;
using LgymApi.Domain.Entities;
using LgymApi.Domain.Enums;
using LgymApi.Domain.ValueObjects;
using LgymApi.Infrastructure.Data;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using NUnit.Framework;

namespace LgymApi.UnitTests;

[TestFixture]
public sealed class RecurringReportAssignmentSchemaTests
{
    [Test]
    public async Task SaveChangesAsync_AfterEnsureCreated_AllowsMultipleReportRequestsForSameRecurringAssignment()
    {
        await using var sqliteConnection = new SqliteConnection("Data Source=:memory:");
        await sqliteConnection.OpenAsync();

        await using var setupContext = CreateDbContext(sqliteConnection);
        await setupContext.Database.EnsureCreatedAsync();

        var trainer = CreateUser("trainer");
        var trainee = CreateUser("trainee");
        var template = CreateTemplate(trainer.Id);
        var assignment = CreateAssignment(trainer.Id, trainee.Id, template.Id);

        var firstRequest = CreateRequest(trainer.Id, trainee.Id, template.Id, assignment.Id, "first");
        var secondRequest = CreateRequest(trainer.Id, trainee.Id, template.Id, assignment.Id, "second");

        await setupContext.AddRangeAsync(trainer, trainee, template, assignment, firstRequest, secondRequest);

        var persistAction = async () => await setupContext.SaveChangesAsync();

        await persistAction.Should().NotThrowAsync();

        await using var verificationContext = CreateDbContext(sqliteConnection);
        var storedRequests = await verificationContext.ReportRequests
            .AsNoTracking()
            .Where(request => request.RecurringReportAssignmentId == assignment.Id)
            .OrderBy(request => request.Note)
            .ToListAsync();

        storedRequests.Should().HaveCount(2);
        storedRequests.Select(request => request.Note).Should().Equal("first", "second");
    }

    [Test]
    public void ModelConfiguration_DeclaresRecurringReportAssignmentIndexAsNonUnique()
    {
        var options = new DbContextOptionsBuilder<AppDbContext>()
            .UseInMemoryDatabase($"recurring-schema-model-{Id<RecurringReportAssignmentSchemaTests>.New():N}")
            .Options;

        using var dbContext = new AppDbContext(options);

        var reportRequestEntity = dbContext.Model.FindEntityType(typeof(ReportRequest));
        var recurringAssignmentIndex = reportRequestEntity!
            .GetIndexes()
            .Single(index => index.Properties.Select(property => property.Name).SequenceEqual([nameof(ReportRequest.RecurringReportAssignmentId)]));

        recurringAssignmentIndex.IsUnique.Should().BeFalse();
    }

    private static AppDbContext CreateDbContext(SqliteConnection sqliteConnection)
        => new(new DbContextOptionsBuilder<AppDbContext>()
            .UseSqlite(sqliteConnection)
            .Options);

    private static User CreateUser(string name)
        => new()
        {
            Id = Id<User>.New(),
            Name = name,
            Email = $"{name}@example.com",
            ProfileRank = "Rookie"
        };

    private static ReportTemplate CreateTemplate(Id<User> trainerId)
        => new()
        {
            Id = Id<ReportTemplate>.New(),
            TrainerId = trainerId,
            Name = "Weekly check-in"
        };

    private static RecurringReportAssignment CreateAssignment(
        Id<User> trainerId,
        Id<User> traineeId,
        Id<ReportTemplate> templateId)
        => new()
        {
            Id = Id<RecurringReportAssignment>.New(),
            TrainerId = trainerId,
            TraineeId = traineeId,
            TemplateId = templateId,
            IntervalValue = 1,
            IntervalUnit = RecurringReportIntervalUnit.Week,
            StartsAt = DateTimeOffset.UtcNow.AddDays(-7),
            IsActive = true,
            Note = "assignment"
        };

    private static ReportRequest CreateRequest(
        Id<User> trainerId,
        Id<User> traineeId,
        Id<ReportTemplate> templateId,
        Id<RecurringReportAssignment> assignmentId,
        string note)
        => new()
        {
            Id = Id<ReportRequest>.New(),
            TrainerId = trainerId,
            TraineeId = traineeId,
            TemplateId = templateId,
            RecurringReportAssignmentId = assignmentId,
            Status = ReportRequestStatus.Pending,
            DueAt = DateTimeOffset.UtcNow.AddDays(7),
            Note = note
        };
}
