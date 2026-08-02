using System.Globalization;
using FluentAssertions;
using LgymApi.Application.BuildingBlocks.Errors;
using LgymApi.Application.BuildingBlocks.Results;
using LgymApi.Application.Features.Reporting;
using LgymApi.Application.Features.Reporting.Models;
using LgymApi.Application.Mapping.Core;
using LgymApi.Application.Platform.Contracts.BackgroundCommands;
using LgymApi.Application.Reporting.ApiAdapters;
using LgymApi.Application.Reporting.Contracts.BackgroundCommands;
using LgymApi.Application.Reporting.Errors;
using LgymApi.Application.Reporting.Persistence;
using LgymApi.Application.Repositories;
using LgymApi.Domain.Entities;
using LgymApi.Domain.Enums;
using LgymApi.Domain.Security;
using LgymApi.Domain.ValueObjects;
using LgymApi.Identity.Contracts;
using LgymApi.Identity.Contracts.Accounts;
using LgymApi.Resources;
using Microsoft.Extensions.Logging.Abstractions;
using NSubstitute;

namespace LgymApi.UnitTests;

public sealed partial class RecurringReportAssignmentServiceTests
{
    [Test]
    public async Task RequestNowAsync_WithNoCurrentRequest_CreatesPendingRequestAndBypassesNextEligibleAt()
    {
        await using var database = CreateDbContext("recurring-request-now-success");
        var trainer = CreateUser();
        var traineeId = Id<User>.New();
        var template = CreateTemplate(trainer.Id);
        var assignment = CreateAssignment(trainer.Id, traineeId, template.Id);
        assignment.Template = template;
        assignment.NextEligibleAt = DateTimeOffset.UtcNow.AddMonths(1);
        database.ReportTemplates.Add(template);
        database.RecurringReportAssignments.Add(assignment);
        await database.SaveChangesAsync();
        var service = CreateService(database, trainer.Id, traineeId, ownsTrainee: true);

        var result = await service.RequestNowAsync(
            ReportingTestData.Account(trainer.Id, true),
            ReportingTestData.AccountId(traineeId),
            assignment.Id);

        result.IsSuccess.Should().BeTrue();
        result.Value.CurrentReportRequest.Should().NotBeNull();
        result.Value.CurrentReportRequest!.Status.Should().Be(ReportRequestStatus.Pending);
        result.Value.CurrentReportRequestId.Should().Be(result.Value.CurrentReportRequest.Id);
        result.Value.LastRequestCreatedAt.Should().Be(result.Value.CurrentReportRequest.CreatedAt);
        result.Value.NextEligibleAt.Should().BeNull();
        database.ReportRequests.Should().ContainSingle();
    }

    [Test]
    public async Task RequestNowAsync_WithCompletedLifecycle_StagesOnceSavesOnceAndCommitsOnce()
    {
        var assignment = CreateProcessingAssignment() with
        {
            NextEligibleAt = DateTimeOffset.UtcNow.AddMonths(1)
        };
        var harness = CreateRequestNowHarness(assignment);

        var result = await harness.Service.RequestNowAsync(
            RequestNowAccount(assignment.TrainerId),
            assignment.TraineeId,
            assignment.Id);

        result.IsSuccess.Should().BeTrue();
        result.Value.CurrentReportRequest.Should().NotBeNull();
        result.Value.CurrentReportRequest!.Status.Should().Be(ReportRequestStatus.Pending);
        result.Value.CurrentReportRequestId.Should().Be(result.Value.CurrentReportRequest.Id);
        result.Value.LastRequestCreatedAt.Should().Be(result.Value.CurrentReportRequest.CreatedAt);
        result.Value.NextEligibleAt.Should().BeNull();
        await harness.RequestPersistence.Received(1).AddRequestAsync(
            Arg.Is<NewReportRequestPersistenceModel>(request =>
                request.Id == result.Value.CurrentReportRequestId
                && request.RecurringReportAssignmentId == assignment.Id
                && request.Status == ReportRequestStatus.Pending),
            Arg.Any<CancellationToken>());
        await harness.AssignmentPersistence.Received(1).UpdateAsync(
            assignment.Id,
            Arg.Is<RecurringReportAssignmentUpdatePersistenceModel>(update =>
                update.CurrentReportRequestId == result.Value.CurrentReportRequestId
                && update.LastRequestCreatedAt == result.Value.LastRequestCreatedAt
                && update.NextEligibleAt == null),
            Arg.Any<CancellationToken>());
        await harness.OutboxWriter.Received(1).StageAsync(
            Arg.Is<ReportRequestCreatedInAppNotificationCommand>(command =>
                command.RequestId == result.Value.CurrentReportRequestId
                && command.TrainerId == assignment.TrainerId
                && command.TraineeId == assignment.TraineeId),
            Arg.Any<CancellationToken>());
        harness.UnitOfWork.SaveCalls.Should().Be(1);
        harness.UnitOfWork.BeginCalls.Should().Be(1);
        harness.Transaction.CommitCalls.Should().Be(1);
        harness.Transaction.RollbackCalls.Should().Be(0);
        await harness.RelationshipPersistence.Received(2).GetAccessAsync(
            assignment.TrainerId,
            assignment.TraineeId,
            Arg.Any<CancellationToken>());
    }

    [Test]
    public async Task RequestNowAsync_WhenCallerIsNotTrainer_ReturnsLocalizedForbiddenBeforeTransaction()
    {
        var assignment = CreateProcessingAssignment();
        var harness = CreateRequestNowHarness(assignment);

        var result = await harness.Service.RequestNowAsync(
            RequestNowAccount(assignment.TrainerId, isTrainer: false),
            assignment.TraineeId,
            assignment.Id);

        result.IsFailure.Should().BeTrue();
        result.Error.Should().BeOfType<ReportingForbiddenError>();
        result.Error.Message.Should().Be(Messages.TrainerRoleRequired);
        harness.UnitOfWork.BeginCalls.Should().Be(0);
        await harness.RelationshipPersistence.DidNotReceiveWithAnyArgs().GetAccessAsync(
            default,
            default,
            default);
    }

    [Test]
    public async Task RequestNowAsync_WhenAssignmentIdIsEmpty_ReturnsBadRequestBeforeTransaction()
    {
        var assignment = CreateProcessingAssignment();
        var harness = CreateRequestNowHarness(assignment);

        var result = await harness.Service.RequestNowAsync(
            RequestNowAccount(assignment.TrainerId),
            assignment.TraineeId,
            Id<RecurringReportAssignment>.Empty);

        result.IsFailure.Should().BeTrue();
        result.Error.Should().BeOfType<InvalidReportingError>();
        result.Error.Message.Should().Be(Messages.FieldRequired);
        harness.UnitOfWork.BeginCalls.Should().Be(0);
        await harness.RelationshipPersistence.DidNotReceiveWithAnyArgs().GetAccessAsync(
            default,
            default,
            default);
    }

    [Test]
    public async Task RequestNowAsync_WhenTraineeIdIsEmpty_ReturnsBadRequestBeforeTransaction()
    {
        var assignment = CreateProcessingAssignment();
        var harness = CreateRequestNowHarness(assignment);

        var result = await harness.Service.RequestNowAsync(
            RequestNowAccount(assignment.TrainerId),
            Id<AccountReference>.Empty,
            assignment.Id);

        result.IsFailure.Should().BeTrue();
        result.Error.Should().BeOfType<InvalidReportingError>();
        result.Error.Message.Should().Be(Messages.UserIdRequired);
        harness.UnitOfWork.BeginCalls.Should().Be(0);
        await harness.RelationshipPersistence.DidNotReceiveWithAnyArgs().GetAccessAsync(
            default,
            default,
            default);
    }

    [Test]
    public async Task RequestNowAsync_WhenPreflightRelationshipIsMissing_ReturnsNotFoundBeforeTransaction()
    {
        var assignment = CreateProcessingAssignment();
        var harness = CreateRequestNowHarness(assignment, preflightRelationship: false);

        var result = await harness.Service.RequestNowAsync(
            RequestNowAccount(assignment.TrainerId),
            assignment.TraineeId,
            assignment.Id);

        result.IsFailure.Should().BeTrue();
        result.Error.Should().BeOfType<ReportingNotFoundError>();
        result.Error.Message.Should().Be(Messages.DidntFind);
        harness.UnitOfWork.BeginCalls.Should().Be(0);
    }

    [Test]
    public async Task RequestNowAsync_WhenLockedAssignmentOwnershipDiffers_ReturnsNotFoundBeforeRelationshipRecheck()
    {
        var assignment = CreateProcessingAssignment();
        var harness = CreateRequestNowHarness(assignment);
        var otherTrainerId = Id<AccountReference>.New();

        var result = await harness.Service.RequestNowAsync(
            RequestNowAccount(otherTrainerId),
            assignment.TraineeId,
            assignment.Id);

        result.IsFailure.Should().BeTrue();
        result.Error.Should().BeOfType<ReportingNotFoundError>();
        harness.UnitOfWork.SaveCalls.Should().Be(0);
        harness.Transaction.RollbackCalls.Should().Be(1);
        await harness.RelationshipPersistence.Received(1).GetAccessAsync(
            otherTrainerId,
            assignment.TraineeId,
            Arg.Any<CancellationToken>());
    }

    [Test]
    public async Task RequestNowAsync_WhenLockedAssignmentIsMissing_ReturnsNotFoundWithoutWrites()
    {
        var assignment = CreateProcessingAssignment();
        var harness = CreateRequestNowHarness(assignment);
        harness.AssignmentPersistence
            .FindByIdForUpdateAsync(assignment.Id, Arg.Any<CancellationToken>())
            .Returns((RecurringReportAssignmentPersistenceModel?)null);

        var result = await harness.Service.RequestNowAsync(
            RequestNowAccount(assignment.TrainerId),
            assignment.TraineeId,
            assignment.Id);

        result.IsFailure.Should().BeTrue();
        result.Error.Should().BeOfType<ReportingNotFoundError>();
        result.Error.Message.Should().Be(Messages.DidntFind);
        harness.UnitOfWork.SaveCalls.Should().Be(0);
        harness.Transaction.RollbackCalls.Should().Be(1);
        await harness.RequestPersistence.DidNotReceiveWithAnyArgs().AddRequestAsync(default!, default);
    }

    [Test]
    public async Task RequestNowAsync_WhenRelationshipEndsAfterPreflight_ReturnsNotFoundWithoutWrites()
    {
        var assignment = CreateProcessingAssignment();
        var harness = CreateRequestNowHarness(assignment, lockedRelationship: false);

        var result = await harness.Service.RequestNowAsync(
            RequestNowAccount(assignment.TrainerId),
            assignment.TraineeId,
            assignment.Id);

        result.IsFailure.Should().BeTrue();
        result.Error.Should().BeOfType<ReportingNotFoundError>();
        harness.UnitOfWork.SaveCalls.Should().Be(0);
        harness.Transaction.RollbackCalls.Should().Be(1);
        await harness.RequestPersistence.DidNotReceiveWithAnyArgs().AddRequestAsync(default!, default);
    }

    [Test]
    public async Task RequestNowAsync_DeletedTemplatePrecedesInactiveWindowAndCommitsDeactivation()
    {
        var assignment = CreateProcessingAssignment();
        assignment = assignment with
        {
            IsActive = false,
            StartsAt = DateTimeOffset.UtcNow.AddDays(2),
            Template = assignment.Template with { IsDeleted = true }
        };
        var harness = CreateRequestNowHarness(assignment);

        var result = await harness.Service.RequestNowAsync(
            RequestNowAccount(assignment.TrainerId),
            assignment.TraineeId,
            assignment.Id);

        result.IsFailure.Should().BeTrue();
        result.Error.Should().BeOfType<ReportingConflictError>();
        result.Error.Message.Should().Be(Messages.RecurringReportTemplateUnavailable);
        harness.UnitOfWork.SaveCalls.Should().Be(1);
        harness.Transaction.CommitCalls.Should().Be(1);
        harness.Transaction.RollbackCalls.Should().Be(0);
        await harness.AssignmentPersistence.Received(1).UpdateAsync(
            assignment.Id,
            Arg.Is<RecurringReportAssignmentUpdatePersistenceModel>(update => !update.IsActive),
            Arg.Any<CancellationToken>());
        await harness.RequestPersistence.DidNotReceiveWithAnyArgs().AddRequestAsync(default!, default);
        await harness.OutboxWriter.DidNotReceiveWithAnyArgs().StageAsync(
            default(ReportRequestCreatedInAppNotificationCommand)!,
            default);
    }

    [Test]
    public async Task RequestNowAsync_WhenTemplateIsDeleted_CommitsDeactivationBeforeConflict()
    {
        var assignment = CreateProcessingAssignment();
        assignment = assignment with { Template = assignment.Template with { IsDeleted = true } };
        var harness = CreateRequestNowHarness(assignment);

        var result = await harness.Service.RequestNowAsync(
            RequestNowAccount(assignment.TrainerId),
            assignment.TraineeId,
            assignment.Id);

        result.IsFailure.Should().BeTrue();
        result.Error.Should().BeOfType<ReportingConflictError>();
        result.Error.Message.Should().Be(Messages.RecurringReportTemplateUnavailable);
        harness.UnitOfWork.SaveCalls.Should().Be(1);
        harness.Transaction.CommitCalls.Should().Be(1);
        harness.Transaction.RollbackCalls.Should().Be(0);
        await harness.AssignmentPersistence.Received(1).UpdateAsync(
            assignment.Id,
            Arg.Is<RecurringReportAssignmentUpdatePersistenceModel>(update => !update.IsActive),
            Arg.Any<CancellationToken>());
    }

    [TestCase(RequestNowUnavailableState.Inactive)]
    [TestCase(RequestNowUnavailableState.NotStarted)]
    [TestCase(RequestNowUnavailableState.Ended)]
    public async Task RequestNowAsync_WhenAssignmentWindowIsUnavailable_ReturnsConflictWithoutWrites(
        RequestNowUnavailableState state)
    {
        var now = DateTimeOffset.UtcNow;
        var assignment = CreateProcessingAssignment();
        assignment = state switch
        {
            RequestNowUnavailableState.Inactive => assignment with { IsActive = false },
            RequestNowUnavailableState.NotStarted => assignment with { StartsAt = now.AddDays(1) },
            RequestNowUnavailableState.Ended => assignment with { EndsAt = now.AddDays(-1) },
            _ => assignment
        };
        var harness = CreateRequestNowHarness(assignment);

        var result = await harness.Service.RequestNowAsync(
            RequestNowAccount(assignment.TrainerId),
            assignment.TraineeId,
            assignment.Id);

        result.IsFailure.Should().BeTrue();
        result.Error.Should().BeOfType<ReportingConflictError>();
        result.Error.Message.Should().Be(Messages.RecurringReportAssignmentUnavailable);
        harness.UnitOfWork.SaveCalls.Should().Be(0);
        harness.Transaction.RollbackCalls.Should().Be(1);
        await harness.RequestPersistence.DidNotReceiveWithAnyArgs().AddRequestAsync(default!, default);
    }

    [TestCase(ReportRequestStatus.Pending)]
    [TestCase(ReportRequestStatus.Expired)]
    [TestCase(ReportRequestStatus.Cancelled)]
    [TestCase((ReportRequestStatus)99)]
    public async Task RequestNowAsync_WhenCurrentStatusIsUnresolved_ReturnsInProgressConflict(
        ReportRequestStatus status)
    {
        var assignment = CreateProcessingAssignment(status);
        await AssertRequestNowInProgressAsync(assignment);
    }

    [Test]
    public async Task RequestNowAsync_WhenCurrentRequestGraphIsMissing_ReturnsInProgressConflict()
    {
        var assignment = CreateProcessingAssignment() with { CurrentReportRequest = null };
        await AssertRequestNowInProgressAsync(assignment);
    }

    [TestCase(RequestNowIncompleteLifecycle.MissingSubmission)]
    [TestCase(RequestNowIncompleteLifecycle.MissingFeedback)]
    [TestCase(RequestNowIncompleteLifecycle.UnreadFeedback)]
    public async Task RequestNowAsync_WhenSubmittedLifecycleIsIncomplete_ReturnsInProgressConflict(
        RequestNowIncompleteLifecycle lifecycle)
    {
        var assignment = CreateProcessingAssignment();
        var request = assignment.CurrentReportRequest!;
        request = lifecycle switch
        {
            RequestNowIncompleteLifecycle.MissingSubmission => request with { Submission = null },
            RequestNowIncompleteLifecycle.MissingFeedback => request with
            {
                Submission = request.Submission! with { TrainerFeedbackAddedAt = null }
            },
            RequestNowIncompleteLifecycle.UnreadFeedback => request with
            {
                Submission = request.Submission! with { TrainerFeedbackReadAt = null }
            },
            _ => request
        };

        await AssertRequestNowInProgressAsync(assignment with { CurrentReportRequest = request });
    }

    [Test]
    public async Task RequestNowAsync_LocalizedConflict_UsesGeneratedPolishAssignmentMessage()
    {
        var previousCulture = CultureInfo.CurrentCulture;
        var previousUiCulture = CultureInfo.CurrentUICulture;
        try
        {
            CultureInfo.CurrentCulture = CultureInfo.GetCultureInfo("pl-PL");
            CultureInfo.CurrentUICulture = CultureInfo.GetCultureInfo("pl-PL");
            var assignment = CreateProcessingAssignment() with { IsActive = false };
            var harness = CreateRequestNowHarness(assignment);

            var result = await harness.Service.RequestNowAsync(
                RequestNowAccount(assignment.TrainerId),
                assignment.TraineeId,
                assignment.Id);

            result.IsFailure.Should().BeTrue();
            result.Error.Should().BeOfType<ReportingConflictError>();
            result.Error.Message.Should().Be("Ten cykl raportu okresowego nie jest teraz aktywny.");
        }
        finally
        {
            CultureInfo.CurrentCulture = previousCulture;
            CultureInfo.CurrentUICulture = previousUiCulture;
        }
    }

    [Test]
    public async Task RequestNowAsync_WhenEnvelopeReceiptIsMissing_RollsBackWithoutSaving()
    {
        var assignment = CreateProcessingAssignment();
        var harness = CreateRequestNowHarness(assignment);
        harness.OutboxWriter.StageAsync(
                Arg.Any<ReportRequestCreatedInAppNotificationCommand>(),
                Arg.Any<CancellationToken>())
            .Returns(new CommandEnvelopeStageResult(null, false));

        var action = () => harness.Service.RequestNowAsync(
            RequestNowAccount(assignment.TrainerId),
            assignment.TraineeId,
            assignment.Id);

        await action.Should().ThrowAsync<InvalidOperationException>();
        harness.UnitOfWork.SaveCalls.Should().Be(0);
        harness.Transaction.CommitCalls.Should().Be(0);
        harness.Transaction.RollbackCalls.Should().Be(1);
    }

    [Test]
    public async Task RequestNowAsync_WhenCancellationArrivesAfterLockedReload_RollsBackBeforeRelationshipRecheck()
    {
        var assignment = CreateProcessingAssignment();
        var harness = CreateRequestNowHarness(assignment);
        using var cancellationSource = new CancellationTokenSource();
        harness.AssignmentPersistence
            .FindByIdForUpdateAsync(assignment.Id, Arg.Any<CancellationToken>())
            .Returns(_ =>
            {
                cancellationSource.Cancel();
                return assignment;
            });

        var action = () => harness.Service.RequestNowAsync(
            RequestNowAccount(assignment.TrainerId),
            assignment.TraineeId,
            assignment.Id,
            cancellationSource.Token);

        var exception = await action.Should().ThrowAsync<OperationCanceledException>();
        exception.Which.CancellationToken.Should().Be(cancellationSource.Token);
        harness.UnitOfWork.SaveCalls.Should().Be(0);
        harness.Transaction.CommitCalls.Should().Be(0);
        harness.Transaction.RollbackCalls.Should().Be(1);
        harness.Transaction.RollbackTokens.Should().ContainSingle().Which.CanBeCanceled.Should().BeFalse();
        harness.Transaction.Operations.Should().Equal("RollbackStarted", "RollbackCompleted", "Dispose");
        await harness.RelationshipPersistence.Received(1).GetAccessAsync(
            assignment.TrainerId,
            assignment.TraineeId,
            cancellationSource.Token);
        await harness.RequestPersistence.DidNotReceiveWithAnyArgs().AddRequestAsync(default!, default);
        await harness.AssignmentPersistence.DidNotReceiveWithAnyArgs().UpdateAsync(default, default!, default);
        await harness.OutboxWriter.DidNotReceiveWithAnyArgs().StageAsync(
            default(ReportRequestCreatedInAppNotificationCommand)!,
            default);
    }

    [Test]
    public async Task RequestNowAsync_WhenCancellationArrivesDuringBusinessDecisionRollback_CompletesRollbackBeforeRethrowing()
    {
        var assignment = CreateProcessingAssignment() with { IsActive = false };
        var harness = CreateRequestNowHarness(assignment);
        using var cancellationSource = new CancellationTokenSource();
        harness.Transaction.OnRollback = _ => cancellationSource.Cancel();

        var action = () => harness.Service.RequestNowAsync(
            RequestNowAccount(assignment.TrainerId),
            assignment.TraineeId,
            assignment.Id,
            cancellationSource.Token);

        var exception = await action.Should().ThrowAsync<OperationCanceledException>();
        exception.Which.CancellationToken.Should().Be(cancellationSource.Token);
        harness.UnitOfWork.SaveCalls.Should().Be(0);
        harness.Transaction.CommitCalls.Should().Be(0);
        harness.Transaction.RollbackCalls.Should().Be(1);
        harness.Transaction.RollbackCompleted.Should().BeTrue();
        harness.Transaction.DisposeCompleted.Should().BeTrue();
        harness.Transaction.RollbackTokens.Should().ContainSingle().Which.CanBeCanceled.Should().BeFalse();
        harness.Transaction.Operations.Should().Equal("RollbackStarted", "RollbackCompleted", "Dispose");
        await harness.RequestPersistence.DidNotReceiveWithAnyArgs().AddRequestAsync(default!, default);
        await harness.AssignmentPersistence.DidNotReceiveWithAnyArgs().UpdateAsync(default, default!, default);
        await harness.OutboxWriter.DidNotReceiveWithAnyArgs().StageAsync(
            default(ReportRequestCreatedInAppNotificationCommand)!,
            default);
    }

    [Test]
    public async Task RequestNowAsync_WhenCancellationArrivesDuringOuterSave_RollsBackEveryStagedEffectWithoutCommit()
    {
        var assignment = CreateProcessingAssignment();
        var harness = CreateRequestNowHarness(assignment);
        using var cancellationSource = new CancellationTokenSource();
        harness.UnitOfWork.OnSave = _ => cancellationSource.Cancel();

        var action = () => harness.Service.RequestNowAsync(
            RequestNowAccount(assignment.TrainerId),
            assignment.TraineeId,
            assignment.Id,
            cancellationSource.Token);

        var exception = await action.Should().ThrowAsync<OperationCanceledException>();
        exception.Which.CancellationToken.Should().Be(cancellationSource.Token);
        await harness.RequestPersistence.Received(1).AddRequestAsync(
            Arg.Any<NewReportRequestPersistenceModel>(),
            cancellationSource.Token);
        await harness.AssignmentPersistence.Received(1).UpdateAsync(
            assignment.Id,
            Arg.Any<RecurringReportAssignmentUpdatePersistenceModel>(),
            cancellationSource.Token);
        await harness.OutboxWriter.Received(1).StageAsync(
            Arg.Any<ReportRequestCreatedInAppNotificationCommand>(),
            cancellationSource.Token);
        harness.UnitOfWork.SaveCalls.Should().Be(1);
        harness.UnitOfWork.SaveTokens.Should().ContainSingle().Which.Should().Be(cancellationSource.Token);
        harness.Transaction.CommitCalls.Should().Be(0);
        harness.Transaction.RollbackCalls.Should().Be(1);
        harness.Transaction.RollbackCompleted.Should().BeTrue();
        harness.Transaction.DisposeCompleted.Should().BeTrue();
        harness.Transaction.RollbackTokens.Should().ContainSingle().Which.CanBeCanceled.Should().BeFalse();
        harness.Transaction.Operations.Should().Equal(
            "StageRequest",
            "StageAssignment",
            "StageEnvelope",
            "Save",
            "RollbackStarted",
            "RollbackCompleted",
            "Dispose");
    }

    [Test]
    public async Task RequestNowAsync_WhenCommitFails_DoesNotRollbackAmbiguousTransaction()
    {
        var assignment = CreateProcessingAssignment();
        var harness = CreateRequestNowHarness(assignment);
        harness.Transaction.CommitException = new InvalidOperationException("commit failed");

        var action = () => harness.Service.RequestNowAsync(
            RequestNowAccount(assignment.TrainerId),
            assignment.TraineeId,
            assignment.Id);

        await action.Should().ThrowAsync<InvalidOperationException>();
        harness.UnitOfWork.SaveCalls.Should().Be(1);
        harness.Transaction.CommitCalls.Should().Be(1);
        harness.Transaction.RollbackCalls.Should().Be(0);
    }

    [Test]
    public async Task RequestNowAsync_WhenRollbackFails_AbortsWithRollbackFailure()
    {
        var assignment = CreateProcessingAssignment(ReportRequestStatus.Pending);
        var harness = CreateRequestNowHarness(assignment);
        var rollbackFailure = new InvalidOperationException("rollback failed");
        harness.Transaction.RollbackException = rollbackFailure;

        var action = () => harness.Service.RequestNowAsync(
            RequestNowAccount(assignment.TrainerId),
            assignment.TraineeId,
            assignment.Id);

        var exception = await action.Should().ThrowAsync<InvalidOperationException>();
        exception.Which.Should().BeSameAs(rollbackFailure);
        harness.UnitOfWork.SaveCalls.Should().Be(0);
        harness.Transaction.CommitCalls.Should().Be(0);
        harness.Transaction.RollbackCalls.Should().Be(1);
    }

    [Test]
    public async Task RequestNowAsync_WhenTransactionDisposalFails_Aborts()
    {
        var assignment = CreateProcessingAssignment(ReportRequestStatus.Pending);
        var harness = CreateRequestNowHarness(assignment);
        var disposalFailure = new InvalidOperationException("dispose failed");
        harness.Transaction.DisposeException = disposalFailure;

        var action = () => harness.Service.RequestNowAsync(
            RequestNowAccount(assignment.TrainerId),
            assignment.TraineeId,
            assignment.Id);

        var exception = await action.Should().ThrowAsync<InvalidOperationException>();
        exception.Which.Should().BeSameAs(disposalFailure);
        harness.UnitOfWork.SaveCalls.Should().Be(0);
        harness.Transaction.RollbackCalls.Should().Be(1);
    }

    [Test]
    public async Task RequestNowAsync_WhenTransactionBeginFails_DoesNotAttemptLockOrWrite()
    {
        var assignment = CreateProcessingAssignment();
        var harness = CreateRequestNowHarness(assignment);
        var beginFailure = new InvalidOperationException("begin failed");
        harness.UnitOfWork.BeginException = beginFailure;

        var action = () => harness.Service.RequestNowAsync(
            RequestNowAccount(assignment.TrainerId),
            assignment.TraineeId,
            assignment.Id);

        var exception = await action.Should().ThrowAsync<InvalidOperationException>();
        exception.Which.Should().BeSameAs(beginFailure);
        harness.UnitOfWork.BeginCalls.Should().Be(1);
        harness.UnitOfWork.SaveCalls.Should().Be(0);
        await harness.AssignmentPersistence.DidNotReceiveWithAnyArgs().FindByIdForUpdateAsync(default, default);
    }

    [Test]
    public async Task RequestNowApiAdapter_ForwardsToExistingServiceContract()
    {
        var service = Substitute.For<IRecurringReportAssignmentService>();
        var trainer = RequestNowAccount(Id<AccountReference>.New());
        var traineeId = Id<AccountReference>.New();
        var assignmentId = Id<RecurringReportAssignment>.New();
        var expected = Result<RecurringReportAssignmentResult, AppError>.Success(new RecurringReportAssignmentResult
        {
            Id = assignmentId
        });
        service.RequestNowAsync(trainer, traineeId, assignmentId, Arg.Any<CancellationToken>())
            .Returns(expected);
        var adapter = new RecurringReportAssignmentApiAdapter(service);

        var result = await adapter.RequestNowAsync(trainer, traineeId, assignmentId);

        result.IsSuccess.Should().BeTrue();
        result.Value.Should().BeSameAs(expected.Value);
        await service.Received(1).RequestNowAsync(trainer, traineeId, assignmentId, Arg.Any<CancellationToken>());
    }

    private static async Task AssertRequestNowInProgressAsync(
        RecurringReportAssignmentPersistenceModel assignment)
    {
        var harness = CreateRequestNowHarness(assignment);

        var result = await harness.Service.RequestNowAsync(
            RequestNowAccount(assignment.TrainerId),
            assignment.TraineeId,
            assignment.Id);

        result.IsFailure.Should().BeTrue();
        result.Error.Should().BeOfType<ReportingConflictError>();
        result.Error.Message.Should().Be(Messages.RecurringReportRequestInProgress);
        harness.UnitOfWork.SaveCalls.Should().Be(0);
        harness.Transaction.RollbackCalls.Should().Be(1);
        await harness.RequestPersistence.DidNotReceiveWithAnyArgs().AddRequestAsync(default!, default);
        await harness.AssignmentPersistence.DidNotReceiveWithAnyArgs().UpdateAsync(default, default!, default);
        await harness.OutboxWriter.DidNotReceiveWithAnyArgs().StageAsync(
            default(ReportRequestCreatedInAppNotificationCommand)!,
            default);
    }

    private static RequestNowHarness CreateRequestNowHarness(
        RecurringReportAssignmentPersistenceModel assignment,
        bool preflightRelationship = true,
        bool lockedRelationship = true)
    {
        var templatePersistence = Substitute.For<IReportTemplatePersistence>();
        var requestPersistence = Substitute.For<IReportRequestSubmissionPersistence>();
        var assignmentPersistence = Substitute.For<IRecurringReportAssignmentPersistence>();
        var relationshipPersistence = Substitute.For<IReportingRelationshipAccessPersistence>();
        var outboxWriter = Substitute.For<ICommandOutboxWriter>();
        var transaction = Substitute.For<IUnitOfWorkTransaction>();
        var unitOfWork = Substitute.For<IUnitOfWork>();
        var unitOfWorkProbe = new RequestNowUnitOfWorkProbe();
        var transactionProbe = new RequestNowTransactionProbe();
        unitOfWork
            .SaveChangesAsync(Arg.Any<CancellationToken>())
            .Returns(call =>
            {
                var cancellationToken = call.Arg<CancellationToken>();
                unitOfWorkProbe.SaveCalls++;
                unitOfWorkProbe.SaveTokens.Add(cancellationToken);
                transactionProbe.Operations.Add("Save");
                unitOfWorkProbe.OnSave?.Invoke(cancellationToken);
                return unitOfWorkProbe.SaveException == null
                    ? Task.FromResult(1)
                    : Task.FromException<int>(unitOfWorkProbe.SaveException);
            });
        unitOfWork
            .BeginTransactionAsync(Arg.Any<CancellationToken>())
            .Returns(_ =>
            {
                unitOfWorkProbe.BeginCalls++;
                return unitOfWorkProbe.BeginException == null
                    ? Task.FromResult(transaction)
                    : Task.FromException<IUnitOfWorkTransaction>(unitOfWorkProbe.BeginException);
            });
        transaction
            .CommitAsync(Arg.Any<CancellationToken>())
            .Returns(_ =>
            {
                transactionProbe.CommitCalls++;
                transactionProbe.Operations.Add("Commit");
                return transactionProbe.CommitException == null
                    ? Task.CompletedTask
                    : Task.FromException(transactionProbe.CommitException);
            });
        transaction
            .RollbackAsync(Arg.Any<CancellationToken>())
            .Returns(call =>
            {
                var cancellationToken = call.Arg<CancellationToken>();
                transactionProbe.RollbackCalls++;
                transactionProbe.RollbackTokens.Add(cancellationToken);
                transactionProbe.Operations.Add("RollbackStarted");
                transactionProbe.OnRollback?.Invoke(cancellationToken);
                if (transactionProbe.RollbackException != null)
                {
                    return Task.FromException(transactionProbe.RollbackException);
                }

                transactionProbe.RollbackCompleted = true;
                transactionProbe.Operations.Add("RollbackCompleted");
                return Task.CompletedTask;
            });
        transaction
            .DisposeAsync()
            .Returns(_ =>
            {
                transactionProbe.Operations.Add("Dispose");
                if (transactionProbe.DisposeException != null)
                {
                    return ValueTask.FromException(transactionProbe.DisposeException);
                }

                transactionProbe.DisposeCompleted = true;
                return ValueTask.CompletedTask;
            });
        var relationshipCall = 0;
        relationshipPersistence
            .GetAccessAsync(Arg.Any<Id<AccountReference>>(), Arg.Any<Id<AccountReference>>(), Arg.Any<CancellationToken>())
            .Returns(_ => new ReportingRelationshipAccessFact(
                relationshipCall++ == 0 ? preflightRelationship : lockedRelationship));
        assignmentPersistence
            .FindByIdForUpdateAsync(assignment.Id, Arg.Any<CancellationToken>())
            .Returns(assignment);
        requestPersistence
            .AddRequestAsync(Arg.Any<NewReportRequestPersistenceModel>(), Arg.Any<CancellationToken>())
            .Returns(_ =>
            {
                transactionProbe.Operations.Add("StageRequest");
                return Task.CompletedTask;
            });
        assignmentPersistence
            .UpdateAsync(
                Arg.Any<Id<RecurringReportAssignment>>(),
                Arg.Any<RecurringReportAssignmentUpdatePersistenceModel>(),
                Arg.Any<CancellationToken>())
            .Returns(_ =>
            {
                transactionProbe.Operations.Add("StageAssignment");
                return Task.CompletedTask;
            });
        outboxWriter.StageAsync(
                Arg.Any<ReportRequestCreatedInAppNotificationCommand>(),
                Arg.Any<CancellationToken>())
            .Returns(_ =>
            {
                transactionProbe.Operations.Add("StageEnvelope");
                return new CommandEnvelopeStageResult("request-now-envelope", false);
            });
        var service = new RecurringReportAssignmentService(
            templatePersistence,
            requestPersistence,
            assignmentPersistence,
            relationshipPersistence,
            ReportingTestData.Mapper(),
            outboxWriter,
            unitOfWork,
            NullLogger<RecurringReportAssignmentService>.Instance);
        return new RequestNowHarness(
            service,
            requestPersistence,
            assignmentPersistence,
            relationshipPersistence,
            outboxWriter,
            unitOfWorkProbe,
            transactionProbe);
    }

    private static AuthenticatedAccountContext RequestNowAccount(
        Id<AccountReference> accountId,
        bool isTrainer = true)
        => new(
            accountId,
            null,
            isTrainer ? [AuthConstants.Roles.Trainer] : [],
            [],
            false,
            false);

    private sealed record RequestNowHarness(
        RecurringReportAssignmentService Service,
        IReportRequestSubmissionPersistence RequestPersistence,
        IRecurringReportAssignmentPersistence AssignmentPersistence,
        IReportingRelationshipAccessPersistence RelationshipPersistence,
        ICommandOutboxWriter OutboxWriter,
        RequestNowUnitOfWorkProbe UnitOfWork,
        RequestNowTransactionProbe Transaction);

    private sealed class RequestNowUnitOfWorkProbe
    {
        public int SaveCalls { get; set; }
        public int BeginCalls { get; set; }
        public List<CancellationToken> SaveTokens { get; } = [];
        public Action<CancellationToken>? OnSave { get; set; }
        public Exception? SaveException { get; set; }
        public Exception? BeginException { get; set; }
    }

    private sealed class RequestNowTransactionProbe
    {
        public int CommitCalls { get; set; }
        public int RollbackCalls { get; set; }
        public bool RollbackCompleted { get; set; }
        public bool DisposeCompleted { get; set; }
        public List<CancellationToken> RollbackTokens { get; } = [];
        public List<string> Operations { get; } = [];
        public Action<CancellationToken>? OnRollback { get; set; }
        public Exception? CommitException { get; set; }
        public Exception? RollbackException { get; set; }
        public Exception? DisposeException { get; set; }
    }

    public enum RequestNowUnavailableState
    {
        Inactive,
        NotStarted,
        Ended
    }

    public enum RequestNowIncompleteLifecycle
    {
        MissingSubmission,
        MissingFeedback,
        UnreadFeedback
    }
}
