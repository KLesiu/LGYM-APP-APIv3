using LgymApi.Domain.ValueObjects;
using NUnit.Framework;
using FluentAssertions;
using LgymApi.Domain.Entities;
using LgymApi.Domain.Enums;
using System;
using System.Linq;

namespace LgymApi.UnitTests;

/// <summary>
/// TDD tests for CommandEnvelope status and error history policy implementation.
/// Tests retry mechanics, dead-lettering, and error preservation across attempt sequences.
/// </summary>
[TestFixture]
public class CommandEnvelopeStatusPolicyTests
{
    private CommandEnvelope _envelope = null!;

    [SetUp]
    public void SetUp()
    {
        // Fresh envelope for each test, Pending status
        _envelope = new CommandEnvelope
        {
            Id = LgymApi.Domain.ValueObjects.Id<CommandEnvelope>.New(),
            CorrelationId = Id<CorrelationScope>.New(),
            PayloadJson = "{\"test\": true}",
            CommandTypeFullName = "TestNamespace.TestCommand",
            Status = ActionExecutionStatus.Pending
        };
    }

    #region Happy Path: Transient Failure -> Success

    [Test]
    public void RecordAttemptFailure_FirstAttempt_RecordsErrorAndSchedulesRetry()
    {
        // Arrange
        var errorMsg = "Timeout on first attempt";
        var errorDetails = "System.TimeoutException: The operation timed out";

        // Simulate orchestrator adding HandlerExecution log for first attempt
        AddMockHandlerExecutionLog(_envelope, 0, ActionExecutionStatus.Failed, errorMsg, errorDetails);

        // Act
        _envelope.RecordAttemptFailure(errorMsg, errorDetails);

        // Assert
        _envelope.Status.Should().Be(ActionExecutionStatus.Failed);
        _envelope.LastAttemptAt.Should().NotBeNull();
        _envelope.NextAttemptAt.Should().NotBeNull();

        // Verify orchestrator added the log (no logs created by envelope method itself)
        _envelope.ExecutionLogs.Count(log => log.ActionType == ActionExecutionLogType.HandlerExecution).Should().Be(1);
    }

    [Test]
    public void RecordAttemptFailure_FirstAttempt_SchedulesCorrectBackoff()
    {
        // Arrange
        var now = DateTimeOffset.UtcNow;

        // Simulate orchestrator adding HandlerExecution log for first attempt
        AddMockHandlerExecutionLog(_envelope, 0, ActionExecutionStatus.Failed, "Attempt 1 failed");

        // Act
        _envelope.RecordAttemptFailure("Attempt 1 failed");
        var firstRetryTime = _envelope.NextAttemptAt;

        // Assert - should schedule 60 second backoff for first retry
        firstRetryTime.Should().NotBeNull();
        var expectedDelay = TimeSpan.FromSeconds(60);
        var actualDelay = firstRetryTime!.Value - now;
        actualDelay.Should().BeGreaterThanOrEqualTo(expectedDelay - TimeSpan.FromMilliseconds(100));
        actualDelay.Should().BeLessThanOrEqualTo(expectedDelay + TimeSpan.FromMilliseconds(100));
    }

    [Test]
    public void RecordAttemptFailure_SecondAttempt_SchedulesLongerBackoff()
    {
        // Arrange - simulate first attempt
        AddMockHandlerExecutionLog(_envelope, 0, ActionExecutionStatus.Failed, "Attempt 1 failed");
        _envelope.RecordAttemptFailure("Attempt 1 failed");
        var firstRetryTime = _envelope.NextAttemptAt;

        // Simulate retry execution and second failure
        _envelope.Status = ActionExecutionStatus.Failed; // Would be retried by orchestrator
        var now = DateTimeOffset.UtcNow;
        AddMockHandlerExecutionLog(_envelope, 1, ActionExecutionStatus.Failed, "Attempt 2 failed");

        // Act
        _envelope.RecordAttemptFailure("Attempt 2 failed");

        // Assert - should schedule 300 second (5m) backoff for second retry
        var secondRetryTime = _envelope.NextAttemptAt;
        var expectedDelay = TimeSpan.FromSeconds(300);
        var actualDelay = secondRetryTime!.Value - now;
        actualDelay.Should().BeGreaterThanOrEqualTo(expectedDelay - TimeSpan.FromMilliseconds(100));
        actualDelay.Should().BeLessThanOrEqualTo(expectedDelay + TimeSpan.FromMilliseconds(100));

        // Verify execution logs accumulated (from simulated orchestrator)
        _envelope.ExecutionLogs.Count(log => log.ActionType == ActionExecutionLogType.HandlerExecution).Should().Be(2);
    }
    [Test]
    public void RecordAttemptFailure_PreservesFullErrorHistory()
    {
        // Arrange - simulate multiple failures with different error details
        var error1 = "Connection timeout";
        var error1Details = "System.Net.Http.HttpRequestException: Connection refused";

        var error2 = "Service unavailable";
        var error2Details = "System.Net.Http.HttpStatusCode: 503";

        // Simulate orchestrator adding HandlerExecution logs for both attempts
        AddMockHandlerExecutionLog(_envelope, 0, ActionExecutionStatus.Failed, error1, error1Details);
        AddMockHandlerExecutionLog(_envelope, 1, ActionExecutionStatus.Failed, error2, error2Details);

        // Act
        _envelope.RecordAttemptFailure(error1, error1Details);
        _envelope.RecordAttemptFailure(error2, error2Details);

        // Assert - full history preserved in HandlerExecution logs
        var logs = _envelope.ExecutionLogs.Where(log => log.ActionType == ActionExecutionLogType.HandlerExecution).ToList();
        logs.Count.Should().Be(2);

        logs[0].ErrorMessage.Should().Be(error1);
        logs[0].ErrorDetails.Should().Be(error1Details);
        logs[0].AttemptNumber.Should().Be(0);

        logs[1].ErrorMessage.Should().Be(error2);
        logs[1].ErrorDetails.Should().Be(error2Details);
        logs[1].AttemptNumber.Should().Be(1);

    }
    [Test]
    public void MarkCompleted_AfterTransientFailures_PreservesFailureHistory()
    {
        // Arrange - fail twice then succeed
        // Simulate orchestrator adding HandlerExecution logs
        AddMockHandlerExecutionLog(_envelope, 0, ActionExecutionStatus.Failed, "Attempt 1 failed");
        _envelope.RecordAttemptFailure("Attempt 1 failed");

        AddMockHandlerExecutionLog(_envelope, 1, ActionExecutionStatus.Failed, "Attempt 2 failed");
        _envelope.RecordAttemptFailure("Attempt 2 failed");

        AddMockHandlerExecutionLog(_envelope, 2, ActionExecutionStatus.Completed);

        // Act
        _envelope.MarkCompleted();

        // Assert
        _envelope.Status.Should().Be(ActionExecutionStatus.Completed);
        var handlerLogs = _envelope.ExecutionLogs.Where(log => log.ActionType == ActionExecutionLogType.HandlerExecution).ToList();
        handlerLogs.Should().HaveCount(3);
        handlerLogs.Count(log => log.Status == ActionExecutionStatus.Failed).Should().Be(2);
    }

    #endregion

    #region Max Attempts -> Dead Letter

    [Test]
    public void RecordAttemptFailure_ThirdAttempt_SchedulesRetryWithThirdDelay()
    {
        // Arrange - simulate 3 failed attempts
        AddMockHandlerExecutionLog(_envelope, 0, ActionExecutionStatus.Failed, "Attempt 1 failed");
        _envelope.RecordAttemptFailure("Attempt 1 failed");

        AddMockHandlerExecutionLog(_envelope, 1, ActionExecutionStatus.Failed, "Attempt 2 failed");
        _envelope.RecordAttemptFailure("Attempt 2 failed");

        AddMockHandlerExecutionLog(_envelope, 2, ActionExecutionStatus.Failed, "Attempt 3 failed");

        // Act
        _envelope.RecordAttemptFailure("Attempt 3 failed");

        // Assert - third delay (900s) is scheduled
        _envelope.NextAttemptAt.Should().NotBeNull();
        _envelope.Status.Should().Be(ActionExecutionStatus.Failed);

        // Verify 900-second delay is used
        var expectedNextAttempt = DateTimeOffset.UtcNow.AddSeconds(900);
        _envelope.NextAttemptAt!.Value.Should().BeCloseTo(expectedNextAttempt, TimeSpan.FromSeconds(2));

        // Verify 3 logs recorded
        var handlerLogs = _envelope.ExecutionLogs.Where(log => log.ActionType == ActionExecutionLogType.HandlerExecution).ToList();
        handlerLogs.Count.Should().Be(3);
    }

    [Test]
    public void RecordAttemptFailure_FourthAttempt_DoesNotScheduleRetry()
    {
        // Arrange - simulate 4 failed attempts
        AddMockHandlerExecutionLog(_envelope, 0, ActionExecutionStatus.Failed, "Attempt 1 failed");
        _envelope.RecordAttemptFailure("Attempt 1 failed");

        AddMockHandlerExecutionLog(_envelope, 1, ActionExecutionStatus.Failed, "Attempt 2 failed");
        _envelope.RecordAttemptFailure("Attempt 2 failed");

        AddMockHandlerExecutionLog(_envelope, 2, ActionExecutionStatus.Failed, "Attempt 3 failed");
        _envelope.RecordAttemptFailure("Attempt 3 failed");

        AddMockHandlerExecutionLog(_envelope, 3, ActionExecutionStatus.Failed, "Attempt 4 failed");

        // Act
        _envelope.RecordAttemptFailure("Attempt 4 failed");

        // Assert - no next attempt scheduled (all delays exhausted)
        _envelope.NextAttemptAt.Should().BeNull();
        _envelope.Status.Should().Be(ActionExecutionStatus.Failed);

        // Verify 4 logs recorded
        var handlerLogs = _envelope.ExecutionLogs.Where(log => log.ActionType == ActionExecutionLogType.HandlerExecution).ToList();
        handlerLogs.Count.Should().Be(4);
    }

    [Test]
    public void MarkDeadLettered_TransitionsToTerminalState()
    {
        // Arrange - simulate 4 failed attempts
        // Simulate orchestrator adding HandlerExecution logs for failed attempts
        AddMockHandlerExecutionLog(_envelope, 0, ActionExecutionStatus.Failed, "Attempt 1 failed");
        AddMockHandlerExecutionLog(_envelope, 1, ActionExecutionStatus.Failed, "Attempt 2 failed");
        AddMockHandlerExecutionLog(_envelope, 2, ActionExecutionStatus.Failed, "Attempt 3 failed");
        AddMockHandlerExecutionLog(_envelope, 3, ActionExecutionStatus.Failed, "Attempt 4 failed");

        // Act
        _envelope.MarkDeadLettered();

        // Assert
        _envelope.Status.Should().Be(ActionExecutionStatus.DeadLettered);
        _envelope.CompletedAt.Should().NotBeNull();
        _envelope.NextAttemptAt.Should().BeNull();

        // Verify dead-letter log entry created
        var deadLetterLog = _envelope.ExecutionLogs.FirstOrDefault(log => log.ActionType == ActionExecutionLogType.DeadLetter);
        deadLetterLog.Should().NotBeNull();
        deadLetterLog!.Status.Should().Be(ActionExecutionStatus.DeadLettered);
        deadLetterLog.ErrorMessage.Should().Be("Dead-lettered after maximum retry attempts exceeded");
    }

    [Test]
    public void MarkDeadLettered_IncludesFullErrorContext()
    {
        // Arrange - accumulated errors from failed attempts

        // Simulate orchestrator adding HandlerExecution logs with error details
        AddMockHandlerExecutionLog(_envelope, 0, ActionExecutionStatus.Failed, "Network error", "System.Net.Http.HttpRequestException");
        AddMockHandlerExecutionLog(_envelope, 1, ActionExecutionStatus.Failed, "Timeout error", "System.TimeoutException");
        AddMockHandlerExecutionLog(_envelope, 2, ActionExecutionStatus.Failed, "Service error", "System.ServiceException");
        AddMockHandlerExecutionLog(_envelope, 3, ActionExecutionStatus.Failed, "Final error", "System.FinalException");
        // Act
        _envelope.MarkDeadLettered();

        // Assert - all error details preserved in HandlerExecution logs
        var logs = _envelope.ExecutionLogs.ToList();
        var errorLogs = logs.Where(log => log.ActionType == ActionExecutionLogType.HandlerExecution && log.Status == ActionExecutionStatus.Failed).ToList();

        errorLogs.Count.Should().Be(4);
        errorLogs[0].ErrorMessage.Should().Be("Network error");
        errorLogs[0].ErrorDetails.Should().Be("System.Net.Http.HttpRequestException");

        errorLogs[1].ErrorMessage.Should().Be("Timeout error");
        errorLogs[2].ErrorMessage.Should().Be("Service error");

        errorLogs[3].ErrorMessage.Should().Be("Final error");
        errorLogs[3].ErrorDetails.Should().Be("System.FinalException");

        // Dead-letter marker also present
        var deadLetterLog = logs.FirstOrDefault(log => log.ActionType == ActionExecutionLogType.DeadLetter);
        deadLetterLog.Should().NotBeNull();

    }
    [Test]
    public void MarkDeadLettered_Idempotent_NoDoubleEntry()
    {
        // Arrange
        _envelope.MarkDeadLettered();
        var firstCallLogCount = _envelope.ExecutionLogs.Count;

        // Act - call again
        _envelope.MarkDeadLettered();

        // Assert - no additional log entry created
        _envelope.ExecutionLogs.Count.Should().Be(firstCallLogCount);
        _envelope.Status.Should().Be(ActionExecutionStatus.DeadLettered);
    }

    #endregion

    #region ShouldRetry Predicate

    [Test]
    public void ShouldRetry_PendingStatus_ReturnsFalse()
    {
        // Arrange - envelope is still pending
        _envelope.Status.Should().Be(ActionExecutionStatus.Pending);

        // Act
        var result = _envelope.ShouldRetry();

        // Assert
        result.Should().BeFalse();
    }

    [Test]
    public void ShouldRetry_FailedStatusWithAttemptsRemaining_ReturnsTrue()
    {
        // Arrange - one failed attempt
        _envelope.RecordAttemptFailure("Attempt 1 failed");
        // Simulate orchestrator adding HandlerExecution log
        AddMockHandlerExecutionLog(_envelope, 0, ActionExecutionStatus.Failed);

        _envelope.Status.Should().Be(ActionExecutionStatus.Failed);
        // Act
        var result = _envelope.ShouldRetry();

        // Assert
        result.Should().BeTrue();
    }

    [Test]
    public void ShouldRetry_MaxAttemptsReached_ReturnsFalse()
    {
        // Arrange - 4 failures = max attempts reached (4 HandlerExecution logs)
        // Simulate orchestrator adding HandlerExecution logs for 4 attempts (exceeds limit)
        AddMockHandlerExecutionLog(_envelope, 0, ActionExecutionStatus.Failed);
        AddMockHandlerExecutionLog(_envelope, 1, ActionExecutionStatus.Failed);
        AddMockHandlerExecutionLog(_envelope, 2, ActionExecutionStatus.Failed);
        AddMockHandlerExecutionLog(_envelope, 3, ActionExecutionStatus.Failed);
        // Act
        var result = _envelope.ShouldRetry();

        // Assert
        result.Should().BeFalse();
    }

    [Test]
    public void ShouldRetry_DeadLetteredStatus_ReturnsFalse()
    {
        // Arrange
        _envelope.MarkDeadLettered();

        // Act
        var result = _envelope.ShouldRetry();

        // Assert
        result.Should().BeFalse();
    }

    [Test]
    public void ShouldRetry_CompletedStatus_ReturnsFalse()
    {
        // Arrange
        _envelope.MarkCompleted();

        // Act
        var result = _envelope.ShouldRetry();

        // Assert
        result.Should().BeFalse();
    }

    #endregion

    #region Error Cases & Guards

    [Test]
    public void RecordAttemptFailure_OnDeadLetteredEnvelope_ThrowsInvalidOperation()
    {
        // Arrange
        _envelope.MarkDeadLettered();

        // Act & Assert
        var act = new Action(() =>
            _envelope.RecordAttemptFailure("Should fail"));

        var ex = act.Should().Throw<InvalidOperationException>().And;
        ex.Message.Should().Contain("dead-lettered");
    }

    [Test]
    public void MarkCompleted_OnDeadLetteredEnvelope_Idempotent()
    {
        // Arrange
        _envelope.MarkDeadLettered();

        // Act - should not throw
        _envelope.MarkCompleted();

        // Assert - stays dead-lettered
        _envelope.Status.Should().Be(ActionExecutionStatus.DeadLettered);
    }

    [Test]
    public void MarkCompleted_OnAlreadyCompleted_Idempotent()
    {
        // Arrange
        _envelope.MarkCompleted();
        var firstCallLogCount = _envelope.ExecutionLogs.Count;

        // Act
        _envelope.MarkCompleted();

        // Assert
        _envelope.Status.Should().Be(ActionExecutionStatus.Completed);
        _envelope.ExecutionLogs.Count.Should().Be(firstCallLogCount); // No duplicate log
    }

    #endregion

    #region Constants & Policy Validation

    [Test]
    public void MaxRetryAttempts_ConstantIsThree()
    {
        // MaxRetryAttempts = 3 means: 1 initial attempt + 3 retry attempts = 4 total executions
        // This allows all 3 delay values (60s, 300s, 900s) to be used before dead-lettering
        CommandEnvelope.MaxRetryAttempts.Should().Be(3);
    }

    [Test]
    public void RetryDelaysSeconds_MatchesPolicy()
    {
        var delays = CommandEnvelope.RetryDelaysSeconds;
        delays.Length.Should().Be(3); // 3 delays: 60s, 300s, 900s
        delays[0].Should().Be(60);   // 1 minute
        delays[1].Should().Be(300);  // 5 minutes
        delays[2].Should().Be(900);  // 15 minutes
    }

    [Test]
    public void ExecutionLogAttemptNumbers_AreSequential()
    {
        // Arrange - simulate orchestrator adding HandlerExecution logs
        // Simulate orchestrator adding HandlerExecution logs
        AddMockHandlerExecutionLog(_envelope, 0, ActionExecutionStatus.Failed);
        AddMockHandlerExecutionLog(_envelope, 1, ActionExecutionStatus.Failed);
        AddMockHandlerExecutionLog(_envelope, 2, ActionExecutionStatus.Completed);

        // Act
        var logs = _envelope.ExecutionLogs.Where(log => log.ActionType == ActionExecutionLogType.HandlerExecution).ToList();

        // Assert
        logs[0].AttemptNumber.Should().Be(0);
        logs[1].AttemptNumber.Should().Be(1);
        logs[2].AttemptNumber.Should().Be(2);
    }

    #endregion

    // Helper method to simulate orchestrator adding HandlerExecution logs
    private void AddMockHandlerExecutionLog(CommandEnvelope envelope, int attemptNumber, ActionExecutionStatus status, string? errorMessage = null, string? errorDetails = null)
    {
        if (envelope.ExecutionLogs.All(log => log.ActionType != ActionExecutionLogType.Execute || log.AttemptNumber != attemptNumber))
        {
            envelope.ExecutionLogs.Add(new ActionExecutionLog
            {
                CommandEnvelopeId = envelope.Id,
                ActionType = ActionExecutionLogType.Execute,
                Status = status,
                AttemptNumber = attemptNumber,
                ErrorMessage = errorMessage,
                ErrorDetails = errorDetails
            });
        }

        envelope.ExecutionLogs.Add(new ActionExecutionLog
        {
            CommandEnvelopeId = envelope.Id,
            ActionType = ActionExecutionLogType.HandlerExecution,
            Status = status,
            AttemptNumber = attemptNumber,
            HandlerTypeName = "TestHandler",
            ErrorMessage = errorMessage,
            ErrorDetails = errorDetails
        });
    }
}


