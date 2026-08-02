using FluentAssertions;
using LgymApi.Application.Features.Reporting;
using LgymApi.Application.Mapping.Core;
using LgymApi.Application.Platform.Contracts.BackgroundCommands;
using LgymApi.Application.Repositories;
using LgymApi.Application.Reporting.Persistence;
using LgymApi.Domain.Entities;
using LgymApi.Domain.Enums;
using LgymApi.Domain.ValueObjects;
using LgymApi.Infrastructure.Data;
using LgymApi.Infrastructure.Repositories.Reporting;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;

namespace LgymApi.IntegrationTests;

[TestFixture]
[NonParallelizable]
[Category("PostgreSql")]
public sealed class PostgreSqlRecurringReportProcessingTests : PostgreSqlIntegrationTestBase
{
    [Test]
    public async Task ProcessDueAssignmentsAsync_WhenFirstAssignmentSaveFails_RollsBackAndProcessesSecond()
    {
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(30));
        var cancellationToken = timeout.Token;
        var scenario = await SeedProcessingScenarioAsync(cancellationToken);

        await using var processingScope = Factory.Services.CreateAsyncScope();
        var serviceProvider = processingScope.ServiceProvider;
        var database = serviceProvider.GetRequiredService<AppDbContext>();
        var requestPersistence = new DuplicateFirstRequestPersistence(
            new ReportRequestSubmissionPersistenceRepository(database),
            database,
            scenario.FirstAssignmentId,
            scenario.FirstCurrentRequestId);
        var observedUnitOfWork = new ObservedUnitOfWork(serviceProvider.GetRequiredService<IUnitOfWork>());
        var service = new RecurringReportAssignmentService(
            serviceProvider.GetRequiredService<IReportTemplatePersistence>(),
            requestPersistence,
            serviceProvider.GetRequiredService<IRecurringReportAssignmentPersistence>(),
            serviceProvider.GetRequiredService<IReportingRelationshipAccessPersistence>(),
            serviceProvider.GetRequiredService<IMapper>(),
            serviceProvider.GetRequiredService<ICommandOutboxWriter>(),
            observedUnitOfWork,
            NullLogger<RecurringReportAssignmentService>.Instance);
        service.GetType().GetConstructors().Single().GetParameters()
            .Should().NotContain(parameter => parameter.ParameterType == typeof(ICommandDispatcher));

        await service.ProcessDueAssignmentsAsync(cancellationToken);

        observedUnitOfWork.SaveChangesCalls.Should().Be(2);
        observedUnitOfWork.CommitCalls.Should().Be(1);
        observedUnitOfWork.RollbackCalls.Should().Be(1);
        requestPersistence.FailedAttemptRequestId.Should().NotBeNull();

        await using var assertionScope = Factory.Services.CreateAsyncScope();
        var assertionDatabase = assertionScope.ServiceProvider.GetRequiredService<AppDbContext>();
        var assignments = await assertionDatabase.RecurringReportAssignments
            .AsNoTracking()
            .OrderBy(assignment => assignment.CreatedAt)
            .ToListAsync(cancellationToken);
        var requests = await assertionDatabase.ReportRequests
            .AsNoTracking()
            .OrderBy(request => request.CreatedAt)
            .ToListAsync(cancellationToken);
        var envelopes = await assertionDatabase.CommandEnvelopes
            .AsNoTracking()
            .ToListAsync(cancellationToken);

        assignments.Should().HaveCount(2);
        assignments.Single(assignment => assignment.Id == scenario.FirstAssignmentId)
            .CurrentReportRequestId.Should().Be(scenario.FirstCurrentRequestId);
        var secondAssignment = assignments.Single(assignment => assignment.Id == scenario.SecondAssignmentId);
        secondAssignment.CurrentReportRequestId.Should().NotBe(scenario.SecondCurrentRequestId);
        requests.Should().HaveCount(3);
        requests.Should().NotContain(request => request.Id == requestPersistence.FailedAttemptRequestId);
        requests.Should().ContainSingle(request => request.Id == secondAssignment.CurrentReportRequestId);
        envelopes.Should().ContainSingle();
        envelopes[0].CommandTypeFullName.Should().Be(
            "LgymApi.BackgroundWorker.Common.Commands.ReportRequestCreatedInAppNotificationCommand");
        envelopes[0].PayloadJson.Should().Contain(secondAssignment.CurrentReportRequestId!.Value.ToString());
        envelopes[0].PayloadJson.Should().NotContain(requestPersistence.FailedAttemptRequestId!.Value.ToString());
    }

    [Test]
    public async Task FindByIdForUpdateAsync_SecondTransactionWaitsThenReloadsCommittedCompleteGraph()
    {
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(30));
        var cancellationToken = timeout.Token;
        var assignmentId = await SeedAssignmentAsync(cancellationToken);

        await using var lockingScope = Factory.Services.CreateAsyncScope();
        var lockingDatabase = lockingScope.ServiceProvider.GetRequiredService<AppDbContext>();
        var lockingRepository = new RecurringReportAssignmentPersistenceRepository(lockingDatabase);
        await using var lockingTransaction = await lockingDatabase.Database.BeginTransactionAsync(cancellationToken);
        AssertCompleteGraph(await lockingRepository.FindByIdForUpdateAsync(assignmentId, cancellationToken));

        var assignment = await lockingDatabase.RecurringReportAssignments
            .SingleAsync(candidate => candidate.Id == assignmentId, cancellationToken);
        assignment.Note = "committed by locking transaction";
        await lockingDatabase.SaveChangesAsync(cancellationToken);

        await using var blockedScope = Factory.Services.CreateAsyncScope();
        var blockedDatabase = blockedScope.ServiceProvider.GetRequiredService<AppDbContext>();
        var blockedRepository = new RecurringReportAssignmentPersistenceRepository(blockedDatabase);
        await using var blockedTransaction = await blockedDatabase.Database.BeginTransactionAsync(cancellationToken);
        var blockedReload = blockedRepository.FindByIdForUpdateAsync(assignmentId, cancellationToken);

        var completionBeforeRelease = async () =>
            await blockedReload.WaitAsync(TimeSpan.FromMilliseconds(500), cancellationToken);

        await completionBeforeRelease.Should().ThrowAsync<TimeoutException>();

        await lockingTransaction.CommitAsync(cancellationToken);

        var blockedResult = await blockedReload.WaitAsync(TimeSpan.FromSeconds(5), cancellationToken);
        blockedResult!.Note.Should().Be("committed by locking transaction");
        AssertCompleteGraph(blockedResult);
        await blockedTransaction.CommitAsync(cancellationToken);
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

    private async Task<ProcessingScenario> SeedProcessingScenarioAsync(CancellationToken cancellationToken)
    {
        var trainer = await SeedUserAsync(
            name: $"recurring-processing-trainer-{Id<User>.New()}",
            email: $"recurring-processing-trainer-{Id<User>.New()}@example.com");
        var trainee = await SeedUserAsync(
            name: $"recurring-processing-trainee-{Id<User>.New()}",
            email: $"recurring-processing-trainee-{Id<User>.New()}@example.com");
        await using var scope = Factory.Services.CreateAsyncScope();
        var database = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var template = new ReportTemplate
        {
            Id = Id<ReportTemplate>.New(),
            TrainerId = trainer.Id,
            Name = "Recurring isolation template"
        };
        var now = DateTimeOffset.UtcNow;
        var first = CreateDueAssignmentGraph(
            trainer.Id,
            trainee.Id,
            template,
            nextEligibleAt: now.AddDays(-2),
            createdAt: now.AddDays(-4));
        var second = CreateDueAssignmentGraph(
            trainer.Id,
            trainee.Id,
            template,
            nextEligibleAt: now.AddDays(-1),
            createdAt: now.AddDays(-3));

        database.AddRange(
            template,
            first.Request,
            first.Submission,
            first.Assignment,
            second.Request,
            second.Submission,
            second.Assignment);
        await database.SaveChangesAsync(cancellationToken);

        return new ProcessingScenario(
            first.Assignment.Id,
            first.Request.Id,
            second.Assignment.Id,
            second.Request.Id);
    }

    private static AssignmentGraph CreateDueAssignmentGraph(
        Id<User> trainerId,
        Id<User> traineeId,
        ReportTemplate template,
        DateTimeOffset nextEligibleAt,
        DateTimeOffset createdAt)
    {
        var request = new ReportRequest
        {
            Id = Id<ReportRequest>.New(),
            TrainerId = trainerId,
            TraineeId = traineeId,
            TemplateId = template.Id,
            Template = template,
            Status = ReportRequestStatus.Submitted,
            SubmittedAt = createdAt.AddDays(1),
            CreatedAt = createdAt
        };
        var submission = new ReportSubmission
        {
            Id = Id<ReportSubmission>.New(),
            ReportRequestId = request.Id,
            ReportRequest = request,
            TraineeId = traineeId,
            PayloadJson = "{}",
            TrainerFeedbackAddedAt = createdAt.AddDays(2),
            TrainerFeedbackReadAt = createdAt.AddDays(3),
            CreatedAt = createdAt.AddDays(1)
        };
        request.Submission = submission;
        var assignment = new RecurringReportAssignment
        {
            Id = Id<RecurringReportAssignment>.New(),
            TrainerId = trainerId,
            TraineeId = traineeId,
            TemplateId = template.Id,
            Template = template,
            IntervalValue = 1,
            IntervalUnit = RecurringReportIntervalUnit.Week,
            StartsAt = createdAt,
            IsActive = true,
            CurrentReportRequestId = request.Id,
            CurrentReportRequest = request,
            NextEligibleAt = nextEligibleAt,
            CreatedAt = createdAt
        };
        return new AssignmentGraph(assignment, request, submission);
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

    private sealed record ProcessingScenario(
        Id<RecurringReportAssignment> FirstAssignmentId,
        Id<ReportRequest> FirstCurrentRequestId,
        Id<RecurringReportAssignment> SecondAssignmentId,
        Id<ReportRequest> SecondCurrentRequestId);

    private sealed record AssignmentGraph(
        RecurringReportAssignment Assignment,
        ReportRequest Request,
        ReportSubmission Submission);

    private sealed class DuplicateFirstRequestPersistence(
        IReportRequestSubmissionPersistence inner,
        AppDbContext database,
        Id<RecurringReportAssignment> failingAssignmentId,
        Id<ReportRequest> duplicateRequestId) : IReportRequestSubmissionPersistence
    {
        public Id<ReportRequest>? FailedAttemptRequestId { get; private set; }

        public Task AddRequestAsync(NewReportRequestPersistenceModel request, CancellationToken cancellationToken = default)
        {
            if (request.RecurringReportAssignmentId == failingAssignmentId)
            {
                FailedAttemptRequestId = request.Id;
                request = request with { Id = duplicateRequestId };
                database.SavingChanges += PreserveExistingPointerForDuplicateFailure;
            }

            return inner.AddRequestAsync(request, cancellationToken);
        }

        private void PreserveExistingPointerForDuplicateFailure(object? sender, SavingChangesEventArgs eventArgs)
        {
            var assignment = database.ChangeTracker
                .Entries<RecurringReportAssignment>()
                .SingleOrDefault(entry => entry.Entity.Id == failingAssignmentId);
            if (assignment is not null)
            {
                assignment.Entity.CurrentReportRequestId = duplicateRequestId;
            }

            database.SavingChanges -= PreserveExistingPointerForDuplicateFailure;
        }

        public Task<ReportRequestPersistenceModel?> FindRequestByIdAsync(Id<ReportRequest> requestId, CancellationToken cancellationToken = default)
            => inner.FindRequestByIdAsync(requestId, cancellationToken);

        public Task<IReadOnlyList<ReportRequestPersistenceModel>> ListPendingOrExpiredByTraineeAsync(Id<LgymApi.Identity.Contracts.AccountReference> traineeId, CancellationToken cancellationToken = default)
            => inner.ListPendingOrExpiredByTraineeAsync(traineeId, cancellationToken);

        public Task SetRequestExpiredAsync(Id<ReportRequest> requestId, CancellationToken cancellationToken = default)
            => inner.SetRequestExpiredAsync(requestId, cancellationToken);

        public Task SetRequestSubmittedAsync(Id<ReportRequest> requestId, DateTimeOffset submittedAt, CancellationToken cancellationToken = default)
            => inner.SetRequestSubmittedAsync(requestId, submittedAt, cancellationToken);

        public Task AddSubmissionAsync(NewReportSubmissionPersistenceModel submission, CancellationToken cancellationToken = default)
            => inner.AddSubmissionAsync(submission, cancellationToken);

        public Task<ReportSubmissionPersistenceModel?> FindSubmissionForTrainerAsync(Id<ReportSubmission> submissionId, Id<LgymApi.Identity.Contracts.AccountReference> trainerId, Id<LgymApi.Identity.Contracts.AccountReference> traineeId, CancellationToken cancellationToken = default)
            => inner.FindSubmissionForTrainerAsync(submissionId, trainerId, traineeId, cancellationToken);

        public Task<ReportSubmissionPersistenceModel?> FindSubmissionForTraineeAsync(Id<ReportSubmission> submissionId, Id<LgymApi.Identity.Contracts.AccountReference> traineeId, CancellationToken cancellationToken = default)
            => inner.FindSubmissionForTraineeAsync(submissionId, traineeId, cancellationToken);

        public Task<IReadOnlyList<ReportSubmissionPersistenceModel>> ListSubmissionsByTraineeAsync(Id<LgymApi.Identity.Contracts.AccountReference> traineeId, CancellationToken cancellationToken = default)
            => inner.ListSubmissionsByTraineeAsync(traineeId, cancellationToken);

        public Task<IReadOnlyList<ReportSubmissionPersistenceModel>> ListSubmissionsByTrainerAndTraineeAsync(Id<LgymApi.Identity.Contracts.AccountReference> trainerId, Id<LgymApi.Identity.Contracts.AccountReference> traineeId, CancellationToken cancellationToken = default)
            => inner.ListSubmissionsByTrainerAndTraineeAsync(trainerId, traineeId, cancellationToken);

        public Task UpdateFeedbackAsync(Id<ReportSubmission> submissionId, ReportSubmissionFeedbackUpdatePersistenceModel update, CancellationToken cancellationToken = default)
            => inner.UpdateFeedbackAsync(submissionId, update, cancellationToken);
    }
}
