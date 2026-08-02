using FluentAssertions;
using LgymApi.Application.Reporting.Persistence;
using LgymApi.Domain.Entities;
using LgymApi.Domain.Enums;
using LgymApi.Domain.ValueObjects;
using LgymApi.Infrastructure.Data;
using LgymApi.Infrastructure.Repositories.Reporting;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;

namespace LgymApi.UnitTests;

[TestFixture]
public sealed class RecurringReportAssignmentRepositoryProviderTests
{
    [Test]
    public async Task SqliteWithoutTransaction_RequiresAnActiveTransaction()
    {
        await using var connection = new SqliteConnection("Data Source=:memory:");
        await connection.OpenAsync();
        await using var database = CreateSqliteDatabase(connection);
        var assignmentId = await SeedAssignmentAsync(database);
        var repository = new RecurringReportAssignmentPersistenceRepository(database);

        var action = () => repository.FindByIdForUpdateAsync(assignmentId);

        await action.Should().ThrowAsync<InvalidOperationException>();
    }

    [Test]
    public async Task SqliteWithTransaction_ReturnsCompleteNonDeletedGraph()
    {
        await using var connection = new SqliteConnection("Data Source=:memory:");
        await connection.OpenAsync();
        await using var database = CreateSqliteDatabase(connection);
        var assignmentId = await SeedAssignmentAsync(database);
        var repository = new RecurringReportAssignmentPersistenceRepository(database);

        await using var transaction = await database.Database.BeginTransactionAsync();
        var result = await repository.FindByIdForUpdateAsync(assignmentId);

        AssertCompleteGraph(result);
    }

    [Test]
    public async Task InMemoryWithoutTransaction_RequiresAnActiveTransaction()
    {
        await using var database = new AppDbContext(new DbContextOptionsBuilder<AppDbContext>()
            .UseInMemoryDatabase($"recurring-lock-{Id<RecurringReportAssignmentRepositoryProviderTests>.New():N}")
            .Options);
        var assignmentId = await SeedAssignmentAsync(database);
        var repository = new RecurringReportAssignmentPersistenceRepository(database);

        var action = () => repository.FindByIdForUpdateAsync(assignmentId);

        await action.Should().ThrowAsync<InvalidOperationException>();
    }

    private static AppDbContext CreateSqliteDatabase(SqliteConnection connection)
    {
        var database = new AppDbContext(new DbContextOptionsBuilder<AppDbContext>()
            .UseSqlite(connection)
            .Options);
        database.Database.EnsureCreated();
        return database;
    }

    private static async Task<Id<RecurringReportAssignment>> SeedAssignmentAsync(AppDbContext database)
    {
        var trainer = new User
        {
            Id = Id<User>.New(),
            Name = "Trainer",
            Email = $"trainer-{Id<User>.New():N}@example.com",
            ProfileRank = "Rookie"
        };
        var trainee = new User
        {
            Id = Id<User>.New(),
            Name = "Trainee",
            Email = $"trainee-{Id<User>.New():N}@example.com",
            ProfileRank = "Rookie"
        };
        var template = new ReportTemplate
        {
            Id = Id<ReportTemplate>.New(),
            TrainerId = trainer.Id,
            Name = "Weekly",
            Fields =
            [
                new ReportTemplateField { Id = Id<ReportTemplateField>.New(), Key = "summary", Order = 2 },
                new ReportTemplateField { Id = Id<ReportTemplateField>.New(), Key = "mood", Order = 1 }
            ]
        };
        var request = new ReportRequest
        {
            Id = Id<ReportRequest>.New(),
            TrainerId = trainer.Id,
            TraineeId = trainee.Id,
            TemplateId = template.Id,
            Template = template,
            Status = ReportRequestStatus.Submitted
        };
        var submission = new ReportSubmission
        {
            Id = Id<ReportSubmission>.New(),
            ReportRequestId = request.Id,
            ReportRequest = request,
            TraineeId = trainee.Id,
            PayloadJson = "{}",
            TrainerFeedbackAddedAt = DateTimeOffset.UtcNow.AddDays(-2),
            TrainerFeedbackReadAt = DateTimeOffset.UtcNow.AddDays(-1)
        };
        request.Submission = submission;
        var assignment = new RecurringReportAssignment
        {
            Id = Id<RecurringReportAssignment>.New(),
            TrainerId = trainer.Id,
            TraineeId = trainee.Id,
            TemplateId = template.Id,
            Template = template,
            IntervalValue = 1,
            IntervalUnit = RecurringReportIntervalUnit.Week,
            StartsAt = DateTimeOffset.UtcNow.AddDays(-7),
            IsActive = true,
            CurrentReportRequestId = request.Id,
            CurrentReportRequest = request
        };
        database.AddRange(trainer, trainee, template, request, submission, assignment);
        await database.SaveChangesAsync();
        database.ChangeTracker.Clear();
        return assignment.Id;
    }

    private static void AssertCompleteGraph(RecurringReportAssignmentPersistenceModel? result)
    {
        result.Should().NotBeNull();
        result!.IsDeleted.Should().BeFalse();
        result.Template.Fields.Select(field => field.Order).Should().BeInAscendingOrder();
        result.CurrentReportRequest.Should().NotBeNull();
        result.CurrentReportRequest!.Template.Fields.Select(field => field.Order).Should().BeInAscendingOrder();
        result.CurrentReportRequest.Submission.Should().NotBeNull();
    }
}
