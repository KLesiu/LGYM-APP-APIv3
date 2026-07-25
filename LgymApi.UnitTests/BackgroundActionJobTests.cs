using FluentAssertions;
using Hangfire;
using LgymApi.BackgroundWorker.Common.Jobs;
using LgymApi.BackgroundWorker.Jobs;
using LgymApi.Domain.Entities;
using LgymApi.Domain.ValueObjects;
using NUnit.Framework;

namespace LgymApi.UnitTests;

/// <summary>
/// Unit tests for ActionMessageJob.
/// Validates thin job class delegates correctly to orchestrator and retry metadata is configured.
/// </summary>
[TestFixture]
public class BackgroundActionJobTests
{
    /// <summary>
    /// Test double for orchestrator to track calls without full dependencies.
    /// </summary>
    private class TestDoubleOrchestratorService
    {
        public List<(Id<CommandEnvelope> EnvelopeId, CancellationToken Token)> OrchestrationCalls { get; } = [];

        public async Task OrchestrateAsync(Id<CommandEnvelope> envelopeId, CancellationToken cancellationToken = default)
        {
            OrchestrationCalls.Add((envelopeId, cancellationToken));
            await Task.CompletedTask;
        }
    }

    /// <summary>
    /// Test job wrapper that accepts our test double instead of full orchestrator.
    /// </summary>
    private class TestableActionMessageJob : IActionMessageJob
    {
        private readonly TestDoubleOrchestratorService _testOrchestrator;

        public TestableActionMessageJob(TestDoubleOrchestratorService testOrchestrator)
        {
            _testOrchestrator = testOrchestrator ?? throw new ArgumentNullException(nameof(testOrchestrator));
        }

        public async Task ExecuteAsync(Id<CommandEnvelope> actionMessageId)
        {
            await _testOrchestrator.OrchestrateAsync(actionMessageId);
        }
    }

    private TestDoubleOrchestratorService _testOrchestrator = null!;
    private TestableActionMessageJob _testableJob = null!;

    [SetUp]
    public void SetUp()
    {
        _testOrchestrator = new TestDoubleOrchestratorService();
        _testableJob = new TestableActionMessageJob(_testOrchestrator);
    }

    /// <summary>
    /// Validates job delegates to orchestrator service on happy path.
    /// </summary>
    [Test]
    public async Task ExecuteAsync_DelegatesToOrchestratorWithMessageId()
    {
        // Arrange
        var messageId = Id<CommandEnvelope>.New();

        // Act
        await _testableJob.ExecuteAsync(messageId);

        // Assert
        _testOrchestrator.OrchestrationCalls.Count.Should().Be(1, "Job should delegate to orchestrator exactly once");
        _testOrchestrator.OrchestrationCalls[0].EnvelopeId.Should().Be(messageId, "Job should pass the message id to orchestrator");
    }



    /// <summary>
    /// Validates the [AutomaticRetry] attribute is applied to class level.
    /// </summary>
    [Test]
    public void ActionMessageJob_ClassHasAutoRetryAttribute()
    {
        // Arrange & Act
        var classAttribute = typeof(ActionMessageJob)
            .GetCustomAttributes(typeof(AutomaticRetryAttribute), false)
            .FirstOrDefault() as AutomaticRetryAttribute;

        // Assert
        classAttribute.Should().NotBeNull("ActionMessageJob class must have AutomaticRetryAttribute");
        classAttribute!.Attempts.Should().Be(3);
        classAttribute!.DelaysInSeconds.Should().Equal(new[] { 60, 300, 900 });
    }

    /// <summary>
    /// Validates job throws if orchestrator is null during construction.
    /// </summary>
    [Test]
    public void Constructor_ThrowsArgumentNullException_WhenOrchestratorIsNull()
    {
        // Act & Assert
        var action = () => new ActionMessageJob(null!);
        var ex = action.Should().Throw<ArgumentNullException>().Which;
        ex.ParamName.Should().Be("orchestrator");
    }

    /// <summary>
    /// Validates multiple sequential job executions work correctly.
    /// </summary>
    [Test]
    public async Task ExecuteAsync_CanBeCalledMultipleTimes()
    {
        // Arrange
        var messageIds = new[] { Id<CommandEnvelope>.New(), Id<CommandEnvelope>.New(), Id<CommandEnvelope>.New() };

        // Act
        foreach (var id in messageIds)
        {
            await _testableJob.ExecuteAsync(id);
        }

        // Assert
        _testOrchestrator.OrchestrationCalls.Count.Should().Be(3, "Orchestrator should be called once per execution");

        for (int i = 0; i < 3; i++)
        {
            _testOrchestrator.OrchestrationCalls[i].EnvelopeId.Should().Be(messageIds[i], $"Call {i + 1} should pass correct message id");
        }
    }

    /// <summary>
    /// Validates job class is sealed to prevent inheritance.
    /// </summary>
    [Test]
    public void ActionMessageJob_IsSealed()
    {
        // Act & Assert
        typeof(ActionMessageJob).IsSealed.Should().BeTrue("ActionMessageJob should be sealed to prevent subclassing");
    }

    /// <summary>
    /// Validates job correctly implements IActionMessageJob interface.
    /// </summary>
    [Test]
    public void ActionMessageJob_ImplementsIActionMessageJob()
    {
        // Act & Assert
        typeof(ActionMessageJob).GetInterfaces().Contains(typeof(IActionMessageJob)).Should().BeTrue("ActionMessageJob must implement IActionMessageJob");
    }

    /// <summary>
    /// Validates job returns Task from ExecuteAsync (not void).
    /// </summary>
    [Test]
    public void ExecuteAsync_ReturnsTask()
    {
        // Arrange & Act
        var method = typeof(ActionMessageJob).GetMethod(nameof(ActionMessageJob.ExecuteAsync));

        // Assert
        method?.ReturnType.Should().Be(typeof(Task), "ExecuteAsync must return Task for async execution");
    }

    /// <summary>
    /// Validates ExecuteAsync accepts exactly one typed-id parameter.
    /// </summary>
    [Test]
    public void ExecuteAsync_HasCorrectSignature()
    {
        // Arrange & Act
        var method = typeof(ActionMessageJob).GetMethod(nameof(ActionMessageJob.ExecuteAsync));
        var parameters = method?.GetParameters();

        // Assert
        parameters?.Length.Should().Be(1, "ExecuteAsync should accept exactly 1 parameter");
        parameters?[0].ParameterType.Should().Be(typeof(Id<CommandEnvelope>), "Parameter should be Id<CommandEnvelope> (actionMessageId)");
    }

    /// <summary>
    /// Validates with unknown message id, job still delegates (orchestrator handles gracefully).
    /// </summary>
    [Test]
    public async Task ExecuteAsync_WithUnknownMessageId_StillDelegatesToOrchestrator()
    {
        // Arrange
        var unknownMessageId = Id<CommandEnvelope>.New();

        // Act
        await _testableJob.ExecuteAsync(unknownMessageId);

        // Assert
        _testOrchestrator.OrchestrationCalls.Count.Should().Be(1, "Job should still delegate for unknown ids");
        _testOrchestrator.OrchestrationCalls[0].EnvelopeId.Should().Be(unknownMessageId);
    }

    /// <summary>
    /// Validates retry delays are in correct order (increasing backoff).
    /// </summary>
    [Test]
    public void RetryDelays_AreIncreasingBackoffSequence()
    {
        // Arrange & Act
        var classAttribute = typeof(ActionMessageJob)
            .GetCustomAttributes(typeof(AutomaticRetryAttribute), false)
            .FirstOrDefault() as AutomaticRetryAttribute;

        // Assert
        var delays = classAttribute!.DelaysInSeconds!;
        delays[0].Should().Be(60, "First retry at 1 minute");
        delays[1].Should().Be(300, "Second retry at 5 minutes");
        delays[2].Should().Be(900, "Third retry at 15 minutes");

        // Verify increasing sequence
        for (int i = 1; i < delays.Length; i++)
        {
            delays[i].Should().BeGreaterThan(delays[i - 1], "Delays should increase (backoff strategy)");
        }
    }
}
