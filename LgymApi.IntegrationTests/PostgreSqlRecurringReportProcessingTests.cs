using FluentAssertions;
using LgymApi.Application.Reporting.Persistence;
using LgymApi.Domain.Entities;
using LgymApi.Domain.Enums;
using LgymApi.Domain.ValueObjects;
using LgymApi.Infrastructure.Data;
using LgymApi.Infrastructure.Repositories.Reporting;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Npgsql;

namespace LgymApi.IntegrationTests;

[TestFixture]
[NonParallelizable]
[Category("PostgreSql")]
public sealed class PostgreSqlRecurringReportProcessingTests : PostgreSqlIntegrationTestBase
{
    [Test]
    public async Task FindByIdForUpdateAsync_SecondTransactionTimesOutThenReloadsCompleteGraphAfterRelease()
    {
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(30));
        var cancellationToken = timeout.Token;
        var assignmentId = await SeedAssignmentAsync(cancellationToken);

        await using var lockingScope = Factory.Services.CreateAsyncScope();
        var lockingDatabase = lockingScope.ServiceProvider.GetRequiredService<AppDbContext>();
        var lockingRepository = new RecurringReportAssignmentPersistenceRepository(lockingDatabase);
        await using var lockingTransaction = await lockingDatabase.Database.BeginTransactionAsync(cancellationToken);
        AssertCompleteGraph(await lockingRepository.FindByIdForUpdateAsync(assignmentId, cancellationToken));

        await using (var blockedScope = Factory.Services.CreateAsyncScope())
        {
            var blockedDatabase = blockedScope.ServiceProvider.GetRequiredService<AppDbContext>();
            var blockedRepository = new RecurringReportAssignmentPersistenceRepository(blockedDatabase);
            await using var blockedTransaction = await blockedDatabase.Database.BeginTransactionAsync(cancellationToken);
            await blockedDatabase.Database.ExecuteSqlRawAsync("SET LOCAL lock_timeout = '1s'", cancellationToken);

            var action = () => blockedRepository.FindByIdForUpdateAsync(assignmentId, cancellationToken);

            var exception = await action.Should().ThrowAsync<InvalidOperationException>();
            exception.Which.InnerException.Should().BeOfType<PostgresException>().Which.SqlState.Should().Be("55P03");
        }

        await lockingTransaction.CommitAsync(cancellationToken);

        await using var reloadedScope = Factory.Services.CreateAsyncScope();
        var reloadedDatabase = reloadedScope.ServiceProvider.GetRequiredService<AppDbContext>();
        var reloadedRepository = new RecurringReportAssignmentPersistenceRepository(reloadedDatabase);
        await using var reloadedTransaction = await reloadedDatabase.Database.BeginTransactionAsync(cancellationToken);

        AssertCompleteGraph(await reloadedRepository.FindByIdForUpdateAsync(assignmentId, cancellationToken));
        await reloadedTransaction.CommitAsync(cancellationToken);
    }

    private async Task<Id<RecurringReportAssignment>> SeedAssignmentAsync(CancellationToken cancellationToken)
    {
        var trainer = await SeedUserAsync(
            name: $"recurring-lock-trainer-{Id<User>.New()}",
            email: $"recurring-lock-trainer-{Id<User>.New()}@example.com");
        var trainee = await SeedUserAsync(
            name: $"recurring-lock-trainee-{Id<User>.New()}",
            email: $"recurring-lock-trainee-{Id<User>.New()}@example.com");
        await using var scope = Factory.Services.CreateAsyncScope();
        var database = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var template = new ReportTemplate
        {
            Id = Id<ReportTemplate>.New(),
            TrainerId = trainer.Id,
            Name = "Weekly report",
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
        database.AddRange(template, request, submission, assignment);
        await database.SaveChangesAsync(cancellationToken);
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
