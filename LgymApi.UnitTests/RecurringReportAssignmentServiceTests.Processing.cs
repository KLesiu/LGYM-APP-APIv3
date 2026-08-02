using System.Reflection;
using FluentAssertions;
using LgymApi.Application.Features.Reporting;
using LgymApi.Application.Mapping.Core;
using LgymApi.Application.Platform.Contracts.BackgroundCommands;
using LgymApi.Application.Reporting.Contracts.BackgroundCommands;
using LgymApi.Application.Reporting.Persistence;
using LgymApi.Application.Repositories;
using LgymApi.Domain.Entities;
using LgymApi.Domain.Enums;
using LgymApi.Domain.ValueObjects;
using LgymApi.Identity.Contracts;
using Microsoft.Extensions.Logging;
using NSubstitute;

namespace LgymApi.UnitTests;

public sealed partial class RecurringReportAssignmentServiceTests
{
    [Test]
    public void ConstructorUsesOutboxWriterAndNotDispatcher()
    {
        var parameterTypes = typeof(RecurringReportAssignmentService)
            .GetConstructors(BindingFlags.Public | BindingFlags.Instance)
            .Should().ContainSingle().Subject
            .GetParameters()
            .Select(parameter => parameter.ParameterType)
            .ToArray();

        parameterTypes.Should().Contain(typeof(ICommandOutboxWriter));
        parameterTypes.Should().NotContain(typeof(ICommandDispatcher));
        parameterTypes.Should().Contain(typeof(ILogger<RecurringReportAssignmentService>));
    }

    [Test]
    public async Task NullEnvelope_RollsBackWithoutSaving()
    {
        var harness = CreateProcessingHarness(CreateProcessingAssignment());
        harness.OutboxWriter.StageAsync(
                Arg.Any<ReportRequestCreatedInAppNotificationCommand>(),
                Arg.Any<CancellationToken>())
            .Returns(new CommandEnvelopeStageResult(null, false));

        await harness.Service.ProcessDueAssignmentsAsync();

        harness.UnitOfWork.SaveCalls.Should().Be(0);
        harness.Transaction.RollbackCalls.Should().Be(1);
        harness.Transaction.CommitCalls.Should().Be(0);
        harness.Logger.Entries.Should().ContainSingle(entry => entry.Outcome == "Failed");
    }

    [Test]
    public async Task CancelledStatus_BlocksEvenWithCompleteFeedback()
    {
        var harness = CreateProcessingHarness(CreateProcessingAssignment(ReportRequestStatus.Cancelled));

        await harness.Service.ProcessDueAssignmentsAsync();

        harness.UnitOfWork.SaveCalls.Should().Be(0);
        harness.Transaction.RollbackCalls.Should().Be(1);
        await harness.OutboxWriter.DidNotReceiveWithAnyArgs().StageAsync(
            Arg.Any<ReportRequestCreatedInAppNotificationCommand>(),
            Arg.Any<CancellationToken>());
        harness.Logger.Entries.Should().ContainSingle(entry => entry.Reason == "CurrentRequestCancelled");
    }

    [Test]
    public async Task Cancellation_RollsBackWithNonCancelableTokenAndRethrows()
    {
        var harness = CreateProcessingHarness(CreateProcessingAssignment());
        using var cancellationSource = new CancellationTokenSource();
        cancellationSource.Cancel();
        harness.AssignmentPersistence
            .FindByIdForUpdateAsync(Arg.Any<Id<RecurringReportAssignment>>(), Arg.Any<CancellationToken>())
            .Returns<Task<RecurringReportAssignmentPersistenceModel?>>(_ =>
                throw new OperationCanceledException(cancellationSource.Token));

        var action = () => harness.Service.ProcessDueAssignmentsAsync(cancellationSource.Token);

        await action.Should().ThrowAsync<OperationCanceledException>();
        harness.Transaction.RollbackCalls.Should().Be(1);
        harness.Transaction.RollbackTokens.Should().ContainSingle().Which.CanBeCanceled.Should().BeFalse();
        harness.Logger.Entries.Should().ContainSingle(entry => entry.Reason == "CancellationRequested");
    }

    [Test]
    public async Task CancellationAfterLockedReload_RollsBackWithNonCancelableTokenAndRethrows()
    {
        var assignment = CreateProcessingAssignment(ReportRequestStatus.Pending);
        var harness = CreateProcessingHarness(assignment);
        using var cancellationSource = new CancellationTokenSource();
        harness.AssignmentPersistence
            .FindByIdForUpdateAsync(assignment.Id, Arg.Any<CancellationToken>())
            .Returns(_ =>
            {
                cancellationSource.Cancel();
                return assignment;
            });

        var action = () => harness.Service.ProcessDueAssignmentsAsync(cancellationSource.Token);

        await action.Should().ThrowAsync<OperationCanceledException>();
        harness.Transaction.RollbackCalls.Should().Be(1);
        harness.Transaction.RollbackTokens.Should().ContainSingle().Which.CanBeCanceled.Should().BeFalse();
        harness.Logger.Entries.Should().ContainSingle(entry => entry.Reason == "CancellationRequested");
        await harness.OutboxWriter.DidNotReceiveWithAnyArgs().StageAsync(
            Arg.Any<ReportRequestCreatedInAppNotificationCommand>(),
            Arg.Any<CancellationToken>());
    }

    [Test]
    public async Task RollbackFailure_AbortsAndDoesNotProcessLaterCandidate()
    {
        var first = CreateProcessingAssignment();
        var second = CreateProcessingAssignment(assignmentId: Id<RecurringReportAssignment>.New());
        var harness = CreateProcessingHarness(first, second);
        harness.OutboxWriter.StageAsync(
                Arg.Any<ReportRequestCreatedInAppNotificationCommand>(),
                Arg.Any<CancellationToken>())
            .Returns(new CommandEnvelopeStageResult(null, false));
        harness.Transaction.RollbackException = new InvalidOperationException("rollback-secret");

        var action = () => harness.Service.ProcessDueAssignmentsAsync();

        await action.Should().ThrowAsync<InvalidOperationException>();
        await harness.AssignmentPersistence.DidNotReceive().FindByIdForUpdateAsync(
            second.Id,
            Arg.Any<CancellationToken>());
    }

    [Test]
    public async Task CommitFailure_AbortsWithoutRollback()
    {
        var harness = CreateProcessingHarness(CreateProcessingAssignment());
        harness.Transaction.CommitException = new InvalidOperationException("commit-secret");

        var action = () => harness.Service.ProcessDueAssignmentsAsync();

        await action.Should().ThrowAsync<InvalidOperationException>();
        harness.Transaction.CommitCalls.Should().Be(1);
        harness.Transaction.RollbackCalls.Should().Be(0);
    }

    [Test]
    public async Task DisposeFailure_AbortsWithoutProcessingLaterCandidate()
    {
        var first = CreateProcessingAssignment();
        var second = CreateProcessingAssignment(assignmentId: Id<RecurringReportAssignment>.New());
        var harness = CreateProcessingHarness(first, second);
        harness.Transaction.DisposeException = new InvalidOperationException("dispose-secret");

        var action = () => harness.Service.ProcessDueAssignmentsAsync();

        await action.Should().ThrowAsync<InvalidOperationException>();
        await harness.AssignmentPersistence.DidNotReceive().FindByIdForUpdateAsync(
            second.Id,
            Arg.Any<CancellationToken>());
        harness.Logger.Entries.Should().ContainSingle(entry => entry.Reason == "DisposeFailed");
    }

    [Test]
    public async Task ListFailure_AbortsBeforeBeginningTransaction()
    {
        var harness = CreateProcessingHarness(CreateProcessingAssignment());
        harness.AssignmentPersistence
            .ListDueAsync(Arg.Any<DateTimeOffset>(), Arg.Any<CancellationToken>())
            .Returns<Task<IReadOnlyList<RecurringReportAssignmentPersistenceModel>>>(_ =>
                throw new InvalidOperationException("list-secret"));

        var action = () => harness.Service.ProcessDueAssignmentsAsync();

        await action.Should().ThrowAsync<InvalidOperationException>();
        harness.UnitOfWork.BeginCalls.Should().Be(0);
        harness.Logger.Entries.Should().BeEmpty();
    }

    [Test]
    public async Task BeginFailure_AbortsWithoutProcessingCandidate()
    {
        var assignment = CreateProcessingAssignment();
        var harness = CreateProcessingHarness(assignment);

        harness.UnitOfWork.BeginException = new InvalidOperationException("begin-secret");

        var action = () => harness.Service.ProcessDueAssignmentsAsync();

        await action.Should().ThrowAsync<InvalidOperationException>();
        harness.UnitOfWork.BeginCalls.Should().Be(1);
        await harness.AssignmentPersistence.DidNotReceive().FindByIdForUpdateAsync(
            assignment.Id,
            Arg.Any<CancellationToken>());
        harness.Logger.Entries.Should().ContainSingle(entry =>
            entry.Outcome == "Aborted" && entry.Reason == "BeginTransactionFailed");
        harness.Logger.AllText.Should().NotContain("begin-secret");
    }

    [Test]
    public async Task LogsCreatedOnlyAfterCommit()
    {
        var harness = CreateProcessingHarness(CreateProcessingAssignment());

        await harness.Service.ProcessDueAssignmentsAsync();

        var entry = harness.Logger.Entries.Should().ContainSingle(log => log.Outcome == "Created").Subject;
        entry.CommitCompleted.Should().BeTrue();
        entry.Exception.Should().BeNull();
        entry.Properties.Keys.Should().BeEquivalentTo(
            "AssignmentId", "RequestId", "EnvelopeId", "Outcome", "Reason", "{OriginalFormat}");
    }

    [Test]
    public async Task LogsDeactivatedOnlyAfterCommit()
    {
        var harness = CreateProcessingHarness(CreateProcessingAssignment(templateDeleted: true));

        await harness.Service.ProcessDueAssignmentsAsync();

        var entry = harness.Logger.Entries.Should().ContainSingle(log => log.Outcome == "Deactivated").Subject;
        entry.CommitCompleted.Should().BeTrue();
        entry.Reason.Should().Be("TemplateDeleted");
        await harness.OutboxWriter.DidNotReceiveWithAnyArgs().StageAsync(
            Arg.Any<ReportRequestCreatedInAppNotificationCommand>(),
            Arg.Any<CancellationToken>());
    }

    [Test]
    public async Task LogsFailedOnlyAfterSuccessfulRollback()
    {
        var harness = CreateProcessingHarness(CreateProcessingAssignment());
        harness.OutboxWriter.StageAsync(
                Arg.Any<ReportRequestCreatedInAppNotificationCommand>(),
                Arg.Any<CancellationToken>())
            .Returns(new CommandEnvelopeStageResult(null, false));

        await harness.Service.ProcessDueAssignmentsAsync();

        var entry = harness.Logger.Entries.Should().ContainSingle(log => log.Outcome == "Failed").Subject;
        entry.RollbackCompleted.Should().BeTrue();
        entry.Reason.Should().Be("EnvelopeMissing");
        entry.Exception.Should().BeNull();
    }

    [Test]
    public async Task AmbiguousCommitLogsAbortedWithoutCreatedOrFailed()
    {
        var harness = CreateProcessingHarness(CreateProcessingAssignment());
        harness.Transaction.CommitException = new InvalidOperationException("commit-user-content");

        var action = () => harness.Service.ProcessDueAssignmentsAsync();

        await action.Should().ThrowAsync<InvalidOperationException>();
        harness.Logger.Entries.Should().ContainSingle(entry =>
            entry.Outcome == "Aborted" && entry.Reason == "CommitFailed");
        harness.Logger.Entries.Should().NotContain(entry =>
            entry.Outcome == "Created" || entry.Outcome == "Failed");
        harness.Logger.AllText.Should().NotContain("commit-user-content");
    }

    [Test]
    public async Task RollbackFailureLogsAbortedWithoutOrdinaryFailed()
    {
        var harness = CreateProcessingHarness(CreateProcessingAssignment());
        harness.OutboxWriter.StageAsync(
                Arg.Any<ReportRequestCreatedInAppNotificationCommand>(),
                Arg.Any<CancellationToken>())
            .Returns(new CommandEnvelopeStageResult(null, false));
        harness.Transaction.RollbackException = new InvalidOperationException("rollback-user-content");

        var action = () => harness.Service.ProcessDueAssignmentsAsync();

        await action.Should().ThrowAsync<InvalidOperationException>();
        harness.Logger.Entries.Should().ContainSingle(entry =>
            entry.Outcome == "Aborted" && entry.Reason == "RollbackFailed");
        harness.Logger.Entries.Should().NotContain(entry => entry.Outcome == "Failed");
        harness.Logger.AllText.Should().NotContain("rollback-user-content");
    }

    [Test]
    public async Task PastDueBlockLogsExactReasonWithoutExceptionMessageOrUserContent()
    {
        var assignment = CreateProcessingAssignment(
            ReportRequestStatus.Pending,
            note: "private-report-note",
            templateName: "private-template-name");
        var harness = CreateProcessingHarness(assignment);

        assignment.NextEligibleAt.Should().BeBefore(DateTimeOffset.UtcNow);

        await harness.Service.ProcessDueAssignmentsAsync();

        var entry = harness.Logger.Entries.Should().ContainSingle().Subject;
        entry.Level.Should().Be(LogLevel.Information);
        entry.Outcome.Should().Be("Skipped");
        entry.Reason.Should().Be("CurrentRequestPending");
        entry.Exception.Should().BeNull();
        entry.Properties.Keys.Should().BeEquivalentTo("AssignmentId", "Outcome", "Reason", "{OriginalFormat}");
        harness.Logger.AllText.Should().NotContain("private-report-note");
        harness.Logger.AllText.Should().NotContain("private-template-name");
    }

    private static ProcessingHarness CreateProcessingHarness(
        params RecurringReportAssignmentPersistenceModel[] assignments)
    {
        var templatePersistence = Substitute.For<IReportTemplatePersistence>();
        var requestPersistence = Substitute.For<IReportRequestSubmissionPersistence>();
        var assignmentPersistence = Substitute.For<IRecurringReportAssignmentPersistence>();
        var relationshipPersistence = Substitute.For<IReportingRelationshipAccessPersistence>();
        var mapper = Substitute.For<IMapper>();
        var outboxWriter = Substitute.For<ICommandOutboxWriter>();
        var transaction = new ProcessingTransaction();
        var unitOfWork = new ProcessingUnitOfWork(transaction);
        var logger = new ProcessingLogger(() => transaction);
        var byId = assignments.ToDictionary(assignment => assignment.Id);
        assignmentPersistence.ListDueAsync(Arg.Any<DateTimeOffset>(), Arg.Any<CancellationToken>())
            .Returns(assignments);
        assignmentPersistence
            .FindByIdForUpdateAsync(Arg.Any<Id<RecurringReportAssignment>>(), Arg.Any<CancellationToken>())
            .Returns(call => byId[call.ArgAt<Id<RecurringReportAssignment>>(0)]);
        outboxWriter.StageAsync(
                Arg.Any<ReportRequestCreatedInAppNotificationCommand>(),
                Arg.Any<CancellationToken>())
            .Returns(new CommandEnvelopeStageResult("processing-envelope", false));

        var service = new RecurringReportAssignmentService(
            templatePersistence,
            requestPersistence,
            assignmentPersistence,
            relationshipPersistence,
            mapper,
            outboxWriter,
            unitOfWork,
            logger);
        return new ProcessingHarness(
            service,
            assignmentPersistence,
            outboxWriter,
            unitOfWork,
            transaction,
            logger);
    }

    private static RecurringReportAssignmentPersistenceModel CreateProcessingAssignment(
        ReportRequestStatus status = ReportRequestStatus.Submitted,
        bool templateDeleted = false,
        string? note = null,
        string templateName = "Processing template",
        Id<RecurringReportAssignment>? assignmentId = null)
    {
        var trainerId = Id<AccountReference>.New();
        var traineeId = Id<AccountReference>.New();
        var template = new ReportTemplatePersistenceModel(
            Id<ReportTemplate>.New(),
            trainerId,
            templateName,
            null,
            DateTimeOffset.UtcNow.AddDays(-30),
            templateDeleted,
            []);
        var request = new ReportRequestPersistenceModel(
            Id<ReportRequest>.New(),
            trainerId,
            traineeId,
            template.Id,
            assignmentId,
            status,
            null,
            DateTimeOffset.UtcNow.AddDays(-10),
            note,
            DateTimeOffset.UtcNow.AddDays(-12),
            false,
            template,
            new ReportSubmissionFeedbackPersistenceModel(
                DateTimeOffset.UtcNow.AddDays(-9),
                DateTimeOffset.UtcNow.AddDays(-8)));
        return new RecurringReportAssignmentPersistenceModel(
            assignmentId ?? Id<RecurringReportAssignment>.New(),
            trainerId,
            traineeId,
            template.Id,
            1,
            RecurringReportIntervalUnit.Week,
            DateTimeOffset.UtcNow.AddDays(-20),
            null,
            true,
            note,
            request.Id,
            request.CreatedAt,
            DateTimeOffset.UtcNow.AddDays(-1),
            DateTimeOffset.UtcNow.AddDays(-20),
            false,
            template,
            request);
    }

    private sealed record ProcessingHarness(
        RecurringReportAssignmentService Service,
        IRecurringReportAssignmentPersistence AssignmentPersistence,
        ICommandOutboxWriter OutboxWriter,
        ProcessingUnitOfWork UnitOfWork,
        ProcessingTransaction Transaction,
        ProcessingLogger Logger);

    private sealed class ProcessingUnitOfWork(ProcessingTransaction transaction) : IUnitOfWork
    {
        public int SaveCalls { get; private set; }
        public int BeginCalls { get; private set; }
        public Exception? SaveException { get; set; }
        public Exception? BeginException { get; set; }

        public Task<int> SaveChangesAsync(CancellationToken cancellationToken = default)
        {
            SaveCalls++;
            return SaveException == null
                ? Task.FromResult(1)
                : Task.FromException<int>(SaveException);
        }

        public Task<IUnitOfWorkTransaction> BeginTransactionAsync(CancellationToken cancellationToken = default)
        {
            BeginCalls++;
            return BeginException == null
                ? Task.FromResult<IUnitOfWorkTransaction>(transaction)
                : Task.FromException<IUnitOfWorkTransaction>(BeginException);
        }
    }

    private sealed class ProcessingTransaction : IUnitOfWorkTransaction
    {
        public int CommitCalls { get; private set; }
        public int RollbackCalls { get; private set; }
        public bool CommitCompleted { get; private set; }
        public bool RollbackCompleted { get; private set; }
        public List<CancellationToken> RollbackTokens { get; } = [];
        public Exception? CommitException { get; set; }
        public Exception? RollbackException { get; set; }
        public Exception? DisposeException { get; set; }

        public Task CommitAsync(CancellationToken cancellationToken = default)
        {
            CommitCalls++;
            if (CommitException != null)
            {
                return Task.FromException(CommitException);
            }

            CommitCompleted = true;
            return Task.CompletedTask;
        }

        public Task RollbackAsync(CancellationToken cancellationToken = default)
        {
            RollbackCalls++;
            RollbackTokens.Add(cancellationToken);
            if (RollbackException != null)
            {
                return Task.FromException(RollbackException);
            }

            RollbackCompleted = true;
            return Task.CompletedTask;
        }

        public ValueTask DisposeAsync()
            => DisposeException == null
                ? ValueTask.CompletedTask
                : ValueTask.FromException(DisposeException);
    }

    private sealed class ProcessingLogger(Func<ProcessingTransaction> transaction) : ILogger<RecurringReportAssignmentService>
    {
        public List<ProcessingLogEntry> Entries { get; } = [];
        public string AllText => string.Join('|', Entries.Select(entry => entry.Message));

        public IDisposable? BeginScope<TState>(TState state) where TState : notnull => null;
        public bool IsEnabled(LogLevel logLevel) => true;

        public void Log<TState>(
            LogLevel logLevel,
            EventId eventId,
            TState state,
            Exception? exception,
            Func<TState, Exception?, string> formatter)
        {
            var properties = state is IEnumerable<KeyValuePair<string, object?>> values
                ? values.ToDictionary(pair => pair.Key, pair => pair.Value)
                : new Dictionary<string, object?>();
            Entries.Add(new ProcessingLogEntry(
                logLevel,
                formatter(state, exception),
                exception,
                properties,
                transaction().CommitCompleted,
                transaction().RollbackCompleted));
        }
    }

    private sealed record ProcessingLogEntry(
        LogLevel Level,
        string Message,
        Exception? Exception,
        IReadOnlyDictionary<string, object?> Properties,
        bool CommitCompleted,
        bool RollbackCompleted)
    {
        public string? Outcome => Properties.GetValueOrDefault("Outcome")?.ToString();
        public string? Reason => Properties.GetValueOrDefault("Reason")?.ToString();
    }
}
