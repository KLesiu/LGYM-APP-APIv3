using FluentAssertions;
using LgymApi.Application.BuildingBlocks.Errors;
using LgymApi.Application.BuildingBlocks.Results;
using LgymApi.Application.Features.Reporting;
using LgymApi.Application.Features.Reporting.Models;
using LgymApi.Application.Mapping.Core;
using LgymApi.Application.Platform.Contracts.BackgroundCommands;
using LgymApi.Application.Reporting.Errors;
using LgymApi.Application.Reporting.Persistence;
using LgymApi.Application.Repositories;
using LgymApi.Domain.Entities;
using LgymApi.Domain.Enums;
using LgymApi.Domain.Security;
using LgymApi.Domain.ValueObjects;
using LgymApi.Identity.Contracts;
using LgymApi.Identity.Contracts.Accounts;
using LgymApi.Infrastructure.Data;
using LgymApi.Resources;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;

namespace LgymApi.IntegrationTests;

[TestFixture]
[NonParallelizable]
[Category("PostgreSql")]
public sealed class PostgreSqlRecurringReportRequestConcurrencyTests : PostgreSqlIntegrationTestBase
{
    private const string CanonicalCommandId =
        "LgymApi.BackgroundWorker.Common.Commands.ReportRequestCreatedInAppNotificationCommand";
    private static readonly TimeSpan GateTimeout = TimeSpan.FromSeconds(10);

    [Test]
    public async Task RequestNow_ConcurrentManualCalls_FirstManualWins()
    {
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(30));
        var cancellationToken = timeout.Token;
        var scenario = await SeedScenarioAsync(DateTimeOffset.UtcNow.AddMonths(1), cancellationToken);
        await using var firstScope = Factory.Services.CreateAsyncScope();
        await using var secondScope = Factory.Services.CreateAsyncScope();
        var first = CreateRaceService(firstScope.ServiceProvider);
        var second = CreateRaceService(secondScope.ServiceProvider);

        var firstTask = first.Service.RequestNowAsync(
            scenario.Trainer,
            scenario.TraineeId,
            scenario.AssignmentId,
            cancellationToken);
        var secondTask = second.Service.RequestNowAsync(
            scenario.Trainer,
            scenario.TraineeId,
            scenario.AssignmentId,
            cancellationToken);

        await WaitUntilBothReachedAsync(first.Gate, second.Gate, cancellationToken);
        first.Gate.ReleaseToLock.TrySetResult(true).Should().BeTrue();
        await first.Gate.LockAcquired.Task.WaitAsync(GateTimeout, cancellationToken);
        second.Gate.ReleaseToLock.TrySetResult(true).Should().BeTrue();
        var results = await Task.WhenAll(firstTask, secondTask).WaitAsync(GateTimeout, cancellationToken);

        results[0].IsSuccess.Should().BeTrue();
        results[1].IsFailure.Should().BeTrue();
        results[1].Error.Should().BeOfType<ReportingConflictError>();
        results[1].Error.Message.Should().Be(Messages.RecurringReportRequestInProgress);
        first.UnitOfWork.SaveChangesCalls.Should().Be(1);
        first.UnitOfWork.CommitCalls.Should().Be(1);
        second.UnitOfWork.SaveChangesCalls.Should().Be(0);
        second.UnitOfWork.RollbackCalls.Should().Be(1);
        await AssertSingleCreationAsync(scenario.AssignmentId, cancellationToken);
        WriteGateEvidence("manual-a", first.Gate, second.Gate, "manual-success/manual-conflict");
    }

    [Test]
    public async Task RequestNow_ConcurrentWithAutomatic_ManualWins()
    {
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(30));
        var cancellationToken = timeout.Token;
        var scenario = await SeedScenarioAsync(DateTimeOffset.UtcNow.AddDays(-1), cancellationToken);
        await using var manualScope = Factory.Services.CreateAsyncScope();
        await using var automaticScope = Factory.Services.CreateAsyncScope();
        var manual = CreateRaceService(manualScope.ServiceProvider);
        var automatic = CreateRaceService(automaticScope.ServiceProvider);

        var manualTask = manual.Service.RequestNowAsync(
            scenario.Trainer,
            scenario.TraineeId,
            scenario.AssignmentId,
            cancellationToken);
        var automaticTask = automatic.Service.ProcessDueAssignmentsAsync(cancellationToken);

        await WaitUntilBothReachedAsync(manual.Gate, automatic.Gate, cancellationToken);
        manual.Gate.ReleaseToLock.TrySetResult(true).Should().BeTrue();
        await manual.Gate.LockAcquired.Task.WaitAsync(GateTimeout, cancellationToken);
        automatic.Gate.ReleaseToLock.TrySetResult(true).Should().BeTrue();
        await Task.WhenAll(manualTask, automaticTask).WaitAsync(GateTimeout, cancellationToken);

        var manualResult = await manualTask;
        manualResult.IsSuccess.Should().BeTrue();
        manual.UnitOfWork.SaveChangesCalls.Should().Be(1);
        manual.UnitOfWork.CommitCalls.Should().Be(1);
        automatic.UnitOfWork.SaveChangesCalls.Should().Be(0);
        automatic.UnitOfWork.RollbackCalls.Should().Be(1);
        await AssertSingleCreationAsync(scenario.AssignmentId, cancellationToken);
        WriteGateEvidence("manual", manual.Gate, automatic.Gate, "manual-success/automatic-skip");
    }

    [Test]
    public async Task RequestNow_ConcurrentWithAutomatic_AutomaticWins()
    {
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(30));
        var cancellationToken = timeout.Token;
        var scenario = await SeedScenarioAsync(DateTimeOffset.UtcNow.AddDays(-1), cancellationToken);
        await using var manualScope = Factory.Services.CreateAsyncScope();
        await using var automaticScope = Factory.Services.CreateAsyncScope();
        var manual = CreateRaceService(manualScope.ServiceProvider);
        var automatic = CreateRaceService(automaticScope.ServiceProvider);

        var manualTask = manual.Service.RequestNowAsync(
            scenario.Trainer,
            scenario.TraineeId,
            scenario.AssignmentId,
            cancellationToken);
        var automaticTask = automatic.Service.ProcessDueAssignmentsAsync(cancellationToken);

        await WaitUntilBothReachedAsync(manual.Gate, automatic.Gate, cancellationToken);
        automatic.Gate.ReleaseToLock.TrySetResult(true).Should().BeTrue();
        await automatic.Gate.LockAcquired.Task.WaitAsync(GateTimeout, cancellationToken);
        manual.Gate.ReleaseToLock.TrySetResult(true).Should().BeTrue();
        await Task.WhenAll(manualTask, automaticTask).WaitAsync(GateTimeout, cancellationToken);

        var manualResult = await manualTask;
        manualResult.IsFailure.Should().BeTrue();
        manualResult.Error.Should().BeOfType<ReportingConflictError>();
        manualResult.Error.Message.Should().Be(Messages.RecurringReportRequestInProgress);
        automatic.UnitOfWork.SaveChangesCalls.Should().Be(1);
        automatic.UnitOfWork.CommitCalls.Should().Be(1);
        manual.UnitOfWork.SaveChangesCalls.Should().Be(0);
        manual.UnitOfWork.RollbackCalls.Should().Be(1);
        await AssertSingleCreationAsync(scenario.AssignmentId, cancellationToken);
        WriteGateEvidence("automatic", automatic.Gate, manual.Gate, "automatic-created/manual-conflict");
    }

    private async Task<RaceScenario> SeedScenarioAsync(
        DateTimeOffset nextEligibleAt,
        CancellationToken cancellationToken)
    {
        var trainer = await SeedUserAsync(
            $"request-now-trainer-{Id<User>.New():N}",
            $"request-now-trainer-{Id<User>.New():N}@example.com");
        var trainee = await SeedUserAsync(
            $"request-now-trainee-{Id<User>.New():N}",
            $"request-now-trainee-{Id<User>.New():N}@example.com");
        await using var scope = Factory.Services.CreateAsyncScope();
        var database = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var template = new ReportTemplate
        {
            Id = Id<ReportTemplate>.New(),
            TrainerId = trainer.Id,
            Name = "Concurrency template"
        };
        var assignment = new RecurringReportAssignment
        {
            Id = Id<RecurringReportAssignment>.New(),
            TrainerId = trainer.Id,
            TraineeId = trainee.Id,
            TemplateId = template.Id,
            Template = template,
            IntervalValue = 1,
            IntervalUnit = RecurringReportIntervalUnit.Week,
            StartsAt = DateTimeOffset.UtcNow.AddDays(-10),
            IsActive = true,
            NextEligibleAt = nextEligibleAt
        };
        database.TrainerTraineeLinks.Add(new TrainerTraineeLink
        {
            Id = Id<TrainerTraineeLink>.New(),
            TrainerId = trainer.Id,
            TraineeId = trainee.Id
        });
        database.AddRange(template, assignment);
        await database.SaveChangesAsync(cancellationToken);
        return new RaceScenario(
            new AuthenticatedAccountContext(
                trainer.Id.Rebind<AccountReference>(),
                null,
                [AuthConstants.Roles.Trainer],
                [],
                false,
                false),
            trainee.Id.Rebind<AccountReference>(),
            assignment.Id);
    }

    private static RaceService CreateRaceService(IServiceProvider serviceProvider)
    {
        var gate = new AssignmentLockGate();
        var assignmentPersistence = new GatedAssignmentPersistence(
            serviceProvider.GetRequiredService<IRecurringReportAssignmentPersistence>(),
            gate);
        var unitOfWork = new ObservedUnitOfWork(serviceProvider.GetRequiredService<IUnitOfWork>());
        var service = new RecurringReportAssignmentService(
            serviceProvider.GetRequiredService<IReportTemplatePersistence>(),
            serviceProvider.GetRequiredService<IReportRequestSubmissionPersistence>(),
            assignmentPersistence,
            serviceProvider.GetRequiredService<IReportingRelationshipAccessPersistence>(),
            serviceProvider.GetRequiredService<IMapper>(),
            serviceProvider.GetRequiredService<ICommandOutboxWriter>(),
            unitOfWork,
            NullLogger<RecurringReportAssignmentService>.Instance);
        return new RaceService(service, gate, unitOfWork);
    }

    private static async Task WaitUntilBothReachedAsync(
        AssignmentLockGate first,
        AssignmentLockGate second,
        CancellationToken cancellationToken)
    {
        await Task.WhenAll(
            first.ReachedLock.Task,
            second.ReachedLock.Task).WaitAsync(GateTimeout, cancellationToken);
        first.ReachedCount.Should().Be(1);
        second.ReachedCount.Should().Be(1);
    }

    private async Task AssertSingleCreationAsync(
        Id<RecurringReportAssignment> assignmentId,
        CancellationToken cancellationToken)
    {
        await using var assertionScope = Factory.Services.CreateAsyncScope();
        var database = assertionScope.ServiceProvider.GetRequiredService<AppDbContext>();
        var assignment = await database.RecurringReportAssignments
            .AsNoTracking()
            .SingleAsync(value => value.Id == assignmentId, cancellationToken);
        var requests = await database.ReportRequests
            .AsNoTracking()
            .Where(request => request.RecurringReportAssignmentId == assignmentId)
            .ToListAsync(cancellationToken);
        var envelopes = await database.CommandEnvelopes
            .AsNoTracking()
            .Where(envelope => envelope.CommandTypeFullName == CanonicalCommandId)
            .ToListAsync(cancellationToken);

        requests.Should().ContainSingle();
        var request = requests[0];
        request.Status.Should().Be(ReportRequestStatus.Pending);
        assignment.CurrentReportRequestId.Should().Be(request.Id);
        assignment.LastRequestCreatedAt.Should().Be(request.CreatedAt);
        assignment.NextEligibleAt.Should().BeNull();
        envelopes.Should().ContainSingle();
        envelopes[0].PayloadJson.Should().Contain(request.Id.Value.ToString());
    }

    private static void WriteGateEvidence(
        string winner,
        AssignmentLockGate winnerGate,
        AssignmentLockGate loserGate,
        string outcomes)
        => TestContext.Progress.WriteLine(
            $"winner={winner}; bothReached={winnerGate.ReachedCount == 1 && loserGate.ReachedCount == 1}; "
            + $"winnerLockAcquired={winnerGate.AcquiredCount == 1}; loserLockAcquired={loserGate.AcquiredCount == 1}; outcomes={outcomes}");

    private sealed record RaceScenario(
        AuthenticatedAccountContext Trainer,
        Id<AccountReference> TraineeId,
        Id<RecurringReportAssignment> AssignmentId);

    private sealed record RaceService(
        RecurringReportAssignmentService Service,
        AssignmentLockGate Gate,
        ObservedUnitOfWork UnitOfWork);

    private sealed class AssignmentLockGate
    {
        private int _reachedCount;
        private int _acquiredCount;

        public TaskCompletionSource<bool> ReachedLock { get; } =
            new(TaskCreationOptions.RunContinuationsAsynchronously);
        public TaskCompletionSource<bool> ReleaseToLock { get; } =
            new(TaskCreationOptions.RunContinuationsAsynchronously);
        public TaskCompletionSource<bool> LockAcquired { get; } =
            new(TaskCreationOptions.RunContinuationsAsynchronously);
        public int ReachedCount => _reachedCount;
        public int AcquiredCount => _acquiredCount;

        public void ReportReached()
        {
            Interlocked.Increment(ref _reachedCount);
            ReachedLock.TrySetResult(true);
        }

        public void ReportAcquired()
        {
            Interlocked.Increment(ref _acquiredCount);
            LockAcquired.TrySetResult(true);
        }
    }

    private sealed class GatedAssignmentPersistence(
        IRecurringReportAssignmentPersistence inner,
        AssignmentLockGate gate) : IRecurringReportAssignmentPersistence
    {
        public Task AddAsync(NewRecurringReportAssignmentPersistenceModel assignment, CancellationToken cancellationToken = default)
            => inner.AddAsync(assignment, cancellationToken);

        public Task<RecurringReportAssignmentPersistenceModel?> FindForTrainerAsync(Id<RecurringReportAssignment> assignmentId, Id<AccountReference> trainerId, Id<AccountReference> traineeId, CancellationToken cancellationToken = default)
            => inner.FindForTrainerAsync(assignmentId, trainerId, traineeId, cancellationToken);

        public Task<RecurringReportAssignmentPersistenceModel?> FindByIdAsync(Id<RecurringReportAssignment> assignmentId, CancellationToken cancellationToken = default)
            => inner.FindByIdAsync(assignmentId, cancellationToken);

        public async Task<RecurringReportAssignmentPersistenceModel?> FindByIdForUpdateAsync(Id<RecurringReportAssignment> assignmentId, CancellationToken cancellationToken = default)
        {
            gate.ReportReached();
            await gate.ReleaseToLock.Task.WaitAsync(GateTimeout, cancellationToken);
            var assignment = await inner.FindByIdForUpdateAsync(assignmentId, cancellationToken);
            gate.ReportAcquired();
            return assignment;
        }

        public Task<RecurringReportAssignmentPersistenceModel?> FindByCurrentRequestAsync(Id<ReportRequest> reportRequestId, CancellationToken cancellationToken = default)
            => inner.FindByCurrentRequestAsync(reportRequestId, cancellationToken);

        public Task<IReadOnlyList<RecurringReportAssignmentPersistenceModel>> ListByTrainerAndTraineeAsync(Id<AccountReference> trainerId, Id<AccountReference> traineeId, CancellationToken cancellationToken = default)
            => inner.ListByTrainerAndTraineeAsync(trainerId, traineeId, cancellationToken);

        public Task<IReadOnlyList<RecurringReportAssignmentPersistenceModel>> ListDueAsync(DateTimeOffset now, CancellationToken cancellationToken = default)
            => inner.ListDueAsync(now, cancellationToken);

        public Task UpdateAsync(Id<RecurringReportAssignment> assignmentId, RecurringReportAssignmentUpdatePersistenceModel update, CancellationToken cancellationToken = default)
            => inner.UpdateAsync(assignmentId, update, cancellationToken);
    }
}
