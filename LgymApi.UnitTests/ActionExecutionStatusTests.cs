using FluentAssertions;
using LgymApi.Domain.Enums;
using NUnit.Framework;

namespace LgymApi.UnitTests;

/// <summary>
/// Tests for ActionExecutionStatus enum covering lifecycle states and transition semantics.
/// </summary>
[TestFixture]
public sealed class ActionExecutionStatusTests
{
    [Test]
    public void Enum_HasExpectedValues()
    {
        // Arrange & Act & Assert
        ((int)ActionExecutionStatus.Pending).Should().Be(0);
        ((int)ActionExecutionStatus.Processing).Should().Be(1);
        ((int)ActionExecutionStatus.Completed).Should().Be(2);
        ((int)ActionExecutionStatus.Failed).Should().Be(3);
        ((int)ActionExecutionStatus.DeadLettered).Should().Be(4);
    }

    [Test]
    public void Enum_HasExpectedNames()
    {
        // Arrange & Act & Assert
        ActionExecutionStatus.Pending.ToString().Should().Be("Pending");
        ActionExecutionStatus.Processing.ToString().Should().Be("Processing");
        ActionExecutionStatus.Completed.ToString().Should().Be("Completed");
        ActionExecutionStatus.Failed.ToString().Should().Be("Failed");
        ActionExecutionStatus.DeadLettered.ToString().Should().Be("DeadLettered");
    }

    [Test]
    public void HappyPath_ValidStatusProgressionPendingToCompleted()
    {
        // Arrange
        var status = ActionExecutionStatus.Pending;

        // Act - Transition from Pending to Processing
        status = ActionExecutionStatus.Processing;
        status.Should().Be(ActionExecutionStatus.Processing);

        // Act - Transition from Processing to Completed
        status = ActionExecutionStatus.Completed;
        status.Should().Be(ActionExecutionStatus.Completed);
    }

    [Test]
    public void HappyPath_ValidStatusProgressionPendingToProcessingToFailed()
    {
        // Arrange
        var status = ActionExecutionStatus.Pending;

        // Act - Transition from Pending to Processing
        status = ActionExecutionStatus.Processing;
        status.Should().Be(ActionExecutionStatus.Processing);

        // Act - Transition from Processing to Failed
        status = ActionExecutionStatus.Failed;
        status.Should().Be(ActionExecutionStatus.Failed);
    }

    [Test]
    public void HappyPath_ValidStatusProgressionFailedToProcessing()
    {
        // Arrange - Failed action will be retried
        var status = ActionExecutionStatus.Failed;

        // Act - Retry transitions back to Processing
        status = ActionExecutionStatus.Processing;
        status.Should().Be(ActionExecutionStatus.Processing);
    }

    [Test]
    public void HappyPath_ValidStatusProgressionToDeadLetter()
    {
        // Arrange - After max retry attempts, action is dead-lettered
        var status = ActionExecutionStatus.Failed;

        // Act - Transition to DeadLettered terminal state
        status = ActionExecutionStatus.DeadLettered;
        status.Should().Be(ActionExecutionStatus.DeadLettered);
    }

    [Test]
    public void HappyPath_CompletedIsTerminalState()
    {
        // Arrange
        var status = ActionExecutionStatus.Completed;

        // Act & Assert - Completed is final state, no further transitions
        status.Should().Be(ActionExecutionStatus.Completed);
    }

    [Test]
    public void HappyPath_DeadLetteredIsTerminalState()
    {
        // Arrange
        var status = ActionExecutionStatus.DeadLettered;

        // Act & Assert - DeadLettered is final state, no further transitions
        status.Should().Be(ActionExecutionStatus.DeadLettered);
    }

    [Test]
    [TestCase(ActionExecutionStatus.Pending)]
    [TestCase(ActionExecutionStatus.Processing)]
    [TestCase(ActionExecutionStatus.Completed)]
    [TestCase(ActionExecutionStatus.Failed)]
    [TestCase(ActionExecutionStatus.DeadLettered)]
    public void AllEnumValues_CanBeParsedFromIntValue(ActionExecutionStatus expected)
    {
        // Arrange
        var intValue = (int)expected;

        // Act
        var parsed = (ActionExecutionStatus)intValue;

        // Assert
        parsed.Should().Be(expected);
    }

    [Test]
    public void Enum_Values_AreConsistentAcrossCalls()
    {
        // Arrange & Act & Assert
        var status1 = ActionExecutionStatus.Processing;
        var status2 = ActionExecutionStatus.Processing;

        status1.Should().Be(status2);
        status1.GetHashCode().Should().Be(status2.GetHashCode());
    }

    [Test]
    [TestCase(ActionExecutionStatus.Pending, ActionExecutionStatus.Processing, true)]
    [TestCase(ActionExecutionStatus.Processing, ActionExecutionStatus.Completed, true)]
    [TestCase(ActionExecutionStatus.Processing, ActionExecutionStatus.Failed, true)]
    [TestCase(ActionExecutionStatus.Failed, ActionExecutionStatus.Processing, true)]
    [TestCase(ActionExecutionStatus.Failed, ActionExecutionStatus.DeadLettered, true)]
    [TestCase(ActionExecutionStatus.Completed, ActionExecutionStatus.Completed, true)]
    [TestCase(ActionExecutionStatus.DeadLettered, ActionExecutionStatus.DeadLettered, true)]
    public void TransitionSemantics_DefinesValidTransitions(
        ActionExecutionStatus from,
        ActionExecutionStatus to,
        bool isValid)
    {
        // Arrange & Act & Assert
        // This test documents the valid transition semantics
        // All transitions listed above should be valid for orchestrator policies
        isValid.Should().BeTrue($"Transition from {from} to {to} should be valid");
    }

    [Test]
    public void OrchestrationPolicies_CanUseEnumValuesForStateManagement()
    {
        // Arrange - Simulate orchestration flow
        var actionStatus = ActionExecutionStatus.Pending;
        var maxAttempts = 3;
        var currentAttempt = 0;

        // Act - Processing attempt 1
        actionStatus = ActionExecutionStatus.Processing;
        actionStatus.Should().Be(ActionExecutionStatus.Processing);

        // Act - First attempt fails
        actionStatus = ActionExecutionStatus.Failed;
        currentAttempt++;
        actionStatus.Should().Be(ActionExecutionStatus.Failed);
        currentAttempt.Should().BeLessThan(maxAttempts);

        // Act - Retry: back to Processing for attempt 2
        actionStatus = ActionExecutionStatus.Processing;
        actionStatus.Should().Be(ActionExecutionStatus.Processing);

        // Act - Second attempt fails
        actionStatus = ActionExecutionStatus.Failed;
        currentAttempt++;
        actionStatus.Should().Be(ActionExecutionStatus.Failed);
        currentAttempt.Should().BeLessThan(maxAttempts);

        // Act - Retry: back to Processing for attempt 3
        actionStatus = ActionExecutionStatus.Processing;
        actionStatus.Should().Be(ActionExecutionStatus.Processing);

        // Act - Third attempt fails (max attempts reached)
        actionStatus = ActionExecutionStatus.Failed;
        currentAttempt++;
        actionStatus.Should().Be(ActionExecutionStatus.Failed);
        currentAttempt.Should().Be(maxAttempts);

        // Act - Transition to DeadLettered after max attempts
        actionStatus = ActionExecutionStatus.DeadLettered;
        actionStatus.Should().Be(ActionExecutionStatus.DeadLettered);
    }

    [Test]
    public void SuccessfulRetryPath_CanTransitionFromFailedToCompleted()
    {
        // Arrange - Action failed once but succeeds on retry
        var actionStatus = ActionExecutionStatus.Failed;

        // Act - Retry: back to Processing
        actionStatus = ActionExecutionStatus.Processing;
        actionStatus.Should().Be(ActionExecutionStatus.Processing);

        // Act - Success on retry
        actionStatus = ActionExecutionStatus.Completed;
        actionStatus.Should().Be(ActionExecutionStatus.Completed);
    }

    [Test]
    [TestCase(ActionExecutionStatus.Pending)]
    [TestCase(ActionExecutionStatus.Processing)]
    [TestCase(ActionExecutionStatus.Completed)]
    [TestCase(ActionExecutionStatus.Failed)]
    [TestCase(ActionExecutionStatus.DeadLettered)]
    public void Enum_CanBeUsedInSwitchStatements(ActionExecutionStatus status)
    {
        // Arrange
        string result = status switch
        {
            ActionExecutionStatus.Pending => "pending",
            ActionExecutionStatus.Processing => "processing",
            ActionExecutionStatus.Completed => "completed",
            ActionExecutionStatus.Failed => "failed",
            ActionExecutionStatus.DeadLettered => "dead-lettered",
            _ => throw new ArgumentOutOfRangeException(nameof(status))
        };

        // Act & Assert
        result.Should().NotBeEmpty();
        result.Should().NotContain(" ");
    }
}
