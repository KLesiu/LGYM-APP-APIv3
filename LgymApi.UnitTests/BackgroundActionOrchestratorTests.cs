using LgymApi.Domain.ValueObjects;
using System.Text.Json;
using LgymApi.Application.Repositories;
using LgymApi.Application.Platform.Contracts.BackgroundCommands;
using LgymApi.BackgroundWorker;
using LgymApi.BackgroundWorker.Actions.Contracts;
using LgymApi.BackgroundWorker.Runtime;
using LgymApi.Application.Platform.Contracts.Serialization;
using LgymApi.Domain.Entities;
using LgymApi.Domain.Enums;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using NUnit.Framework;
using FluentAssertions;
using ApplicationActionCommand = LgymApi.Application.Platform.Contracts.BackgroundCommands.IActionCommand;

namespace LgymApi.UnitTests;

/// <summary>
/// Tests for BackgroundActionOrchestratorService covering:
/// - Happy path: multiple handlers succeed in parallel
/// - Failure path: one handler fails while others continue
/// - Retry behavior with attempt increment
/// - Dead-letter transition after max attempts with full error context
/// - Exact-type-only matching (base/derived mismatch rejected)
/// - Zero-handler path behavior
/// </summary>
[TestFixture]
public sealed class BackgroundActionOrchestratorTests
{
    private FakeCommandEnvelopeRepository _repository = null!;
    private FakeUnitOfWork _unitOfWork = null!;
    private Microsoft.Extensions.DependencyInjection.ServiceProvider _serviceProvider = null!;
    private CommandContractRegistry _commandContractRegistry = null!;

    [SetUp]
    public void SetUp()
    {
        _repository = new FakeCommandEnvelopeRepository();
        _unitOfWork = new FakeUnitOfWork();
        _repository.UnitOfWork = _unitOfWork;
        _commandContractRegistry = CommandContractRegistry.CreateForTesting(
        [
            new CommandContract(
                "Tests.BackgroundActionOrchestrator.TestCommand",
                typeof(TestCommand),
                typeof(TestCommand).FullName!,
                [
                    typeof(TestActionHandler1),
                    typeof(TestActionHandler2),
                    typeof(TestActionHandlerSuccess),
                    typeof(TestActionHandlerFailure),
                    typeof(ScopeAwareHandler),
                    typeof(ConcurrencyTestHandler),
                    typeof(CancellationAwareHandler)
                ]),
            new CommandContract(
                "Tests.BackgroundActionOrchestrator.DerivedTestCommand",
                typeof(DerivedTestCommand),
                typeof(DerivedTestCommand).FullName!,
                [typeof(DerivedTestActionHandler)])
        ]);
    }

    [TearDown]
    public void TearDown()
    {
        _serviceProvider?.Dispose();
    }

    [Test]
    public async Task OrchestrateAsync_HappyPath_TwoHandlersSucceed()
    {
        // Arrange
        var envelopeId = Id<CommandEnvelope>.New();
        var command = new TestCommand { Value = "test" };
        var envelope = CreateEnvelope(envelopeId, command);
        _repository.AddEnvelope(envelope);

        var services = new ServiceCollection();
        services.AddScoped<IBackgroundAction<TestCommand>, TestActionHandler1>();
        services.AddScoped<IBackgroundAction<TestCommand>, TestActionHandler2>();
        services.AddSingleton<ILogger<BackgroundActionOrchestratorService>>(_ => new FakeLogger());
        _serviceProvider = services.BuildServiceProvider();

        var orchestrator = CreateOrchestrator();

        // Act
        await orchestrator.OrchestrateAsync(envelopeId);

        // Assert
        envelope.Status.Should().Be(ActionExecutionStatus.Completed);
        envelope.CompletedAt.Should().NotBeNull();
        _unitOfWork.SaveCallCount.Should().BeGreaterThanOrEqualTo(2);
    }

    [Test]
    public async Task OrchestrateAsync_OneHandlerFails_OthersContinue()
    {
        // Arrange
        var envelopeId = Id<CommandEnvelope>.New();
        var command = new TestCommand { Value = "test" };
        var envelope = CreateEnvelope(envelopeId, command);
        _repository.AddEnvelope(envelope);

        var services = new ServiceCollection();
        services.AddScoped<IBackgroundAction<TestCommand>, TestActionHandlerSuccess>();
        services.AddScoped<IBackgroundAction<TestCommand>, TestActionHandlerFailure>();
        services.AddSingleton<ILogger<BackgroundActionOrchestratorService>>(_ => new FakeLogger());
        _serviceProvider = services.BuildServiceProvider();

        var orchestrator = CreateOrchestrator();
        var startedAt = DateTimeOffset.UtcNow;

        // Act & Assert - Should throw because one handler failed
        var ex = await FluentActions.Invoking(async () =>
            await orchestrator.OrchestrateAsync((Id<CommandEnvelope>)envelopeId)).Should().ThrowAsync<InvalidOperationException>();

        // Assert - Failure recorded and exception thrown for Hangfire retry
        ex.Which.Message.Should().Contain("Retry scheduled");
        envelope.Status.Should().Be(ActionExecutionStatus.Failed);
        envelope.ExecutionLogs.Count.Should().BeGreaterThanOrEqualTo(1);
        envelope.NextAttemptAt.Should().NotBeNull();
        (envelope.NextAttemptAt!.Value - startedAt).Should().BeCloseTo(TimeSpan.FromSeconds(60), TimeSpan.FromSeconds(2));
        // First failed orchestration should schedule first retry delay even with multiple handlers.
    }

    [Test]
    public async Task OrchestrateAsync_RetryIncrement_AfterFailure()
    {
        // Arrange
        var envelopeId = Id<CommandEnvelope>.New();
        var command = new TestCommand { Value = "test" };
        var envelope = CreateEnvelope(envelopeId, command);
        _repository.AddEnvelope(envelope);

        var services = new ServiceCollection();
        services.AddScoped<IBackgroundAction<TestCommand>, TestActionHandlerFailure>();
        services.AddSingleton<ILogger<BackgroundActionOrchestratorService>>(_ => new FakeLogger());
        _serviceProvider = services.BuildServiceProvider();

        var orchestrator = CreateOrchestrator();

        // Act & Assert
        await FluentActions.Invoking(async () =>
            await orchestrator.OrchestrateAsync((Id<CommandEnvelope>)envelopeId)).Should().ThrowAsync<InvalidOperationException>();

        // Assert - Failure recorded, retry scheduled, exception thrown
        envelope.Status.Should().Be(ActionExecutionStatus.Failed);
        envelope.LastAttemptAt.Should().NotBeNull();
        envelope.NextAttemptAt.Should().NotBeNull();
    }

    [Test]
    public async Task OrchestrateAsync_DeadLettered_AfterMaxAttempts()
    {
        // Arrange
        var envelopeId = Id<CommandEnvelope>.New();
        var command = new TestCommand { Value = "test" };
        var envelope = CreateEnvelope(envelopeId, command);

        // Simulate 3 prior failed executions so current run exceeds retry limit.
        AddExecuteAttemptLog(envelope, 0, ActionExecutionStatus.Failed, "Attempt 1 failed");
        envelope.RecordAttemptFailure("Attempt 1 failed");

        AddExecuteAttemptLog(envelope, 1, ActionExecutionStatus.Failed, "Attempt 2 failed");
        envelope.RecordAttemptFailure("Attempt 2 failed");

        AddExecuteAttemptLog(envelope, 2, ActionExecutionStatus.Failed, "Attempt 3 failed");
        envelope.RecordAttemptFailure("Attempt 3 failed");

        _repository.AddEnvelope(envelope);

        var services = new ServiceCollection();
        services.AddScoped<IBackgroundAction<TestCommand>, TestActionHandlerFailure>();
        services.AddSingleton<ILogger<BackgroundActionOrchestratorService>>(_ => new FakeLogger());
        _serviceProvider = services.BuildServiceProvider();

        var orchestrator = CreateOrchestrator();

        // Act
        await orchestrator.OrchestrateAsync(envelopeId);

        // Assert
        envelope.Status.Should().Be(ActionExecutionStatus.DeadLettered);
        envelope.CompletedAt.Should().NotBeNull();
        envelope.ExecutionLogs.Any(log => log.ActionType == ActionExecutionLogType.DeadLetter).Should().BeTrue();
    }

    [Test]
    public async Task OrchestrateAsync_ExactTypeOnly_NoPolymorphicMatching()
    {
        // Arrange
        var envelopeId = Id<CommandEnvelope>.New();
        var derivedCommand = new DerivedTestCommand { Value = "derived", Extra = 42 };
        var envelope = CreateEnvelope(envelopeId, derivedCommand);
        _repository.AddEnvelope(envelope);

        var services = new ServiceCollection();
        services.AddScoped<IBackgroundAction<TestCommand>, TestActionHandler1>(); // Base type handler
        services.AddSingleton<ILogger<BackgroundActionOrchestratorService>>(_ => new FakeLogger());
        _serviceProvider = services.BuildServiceProvider();

        var orchestrator = CreateOrchestrator();

        // Act
        await orchestrator.OrchestrateAsync(envelopeId);

        // Assert - No handlers should match, zero-handler path
        envelope.Status.Should().Be(ActionExecutionStatus.Completed);
        envelope.CompletedAt.Should().NotBeNull();
    }

    [Test]
    public async Task OrchestrateAsync_ZeroHandlers_CompletesWithWarning()
    {
        // Arrange
        var envelopeId = Id<CommandEnvelope>.New();
        var command = new TestCommand { Value = "test" };
        var envelope = CreateEnvelope(envelopeId, command);
        _repository.AddEnvelope(envelope);

        var services = new ServiceCollection();
        // No handlers registered
        services.AddSingleton<ILogger<BackgroundActionOrchestratorService>>(_ => new FakeLogger());
        _serviceProvider = services.BuildServiceProvider();

        var orchestrator = CreateOrchestrator();

        // Act
        await orchestrator.OrchestrateAsync(envelopeId);

        // Assert
        envelope.Status.Should().Be(ActionExecutionStatus.Completed);
        envelope.CompletedAt.Should().NotBeNull();
    }

    [Test]
    public async Task OrchestrateAsync_EnvelopeNotFound_Skips()
    {
        // Arrange
        var envelopeId = Id<CommandEnvelope>.New();

        var services = new ServiceCollection();
        services.AddSingleton<ILogger<BackgroundActionOrchestratorService>>(_ => new FakeLogger());
        _serviceProvider = services.BuildServiceProvider();

        var orchestrator = CreateOrchestrator();

        // Act
        await orchestrator.OrchestrateAsync(envelopeId);

        // Assert
        _unitOfWork.SaveCallCount.Should().Be(0);
    }

    [Test]
    public async Task OrchestrateAsync_CancelledBeforeHandlerExecution_RecordsRecoverableFailure()
    {
        var envelopeId = Id<CommandEnvelope>.New();
        var envelope = CreateEnvelope(envelopeId, new TestCommand { Value = "cancelled" });
        _repository.AddEnvelope(envelope);
        var services = new ServiceCollection();
        services.AddScoped<IBackgroundAction<TestCommand>, TestActionHandler1>();
        services.AddSingleton<ILogger<BackgroundActionOrchestratorService>>(_ => new FakeLogger());
        _serviceProvider = services.BuildServiceProvider();
        var orchestrator = CreateOrchestrator();
        using var cancellationSource = new CancellationTokenSource();
        cancellationSource.Cancel();

        var action = () => orchestrator.OrchestrateAsync(envelopeId, cancellationSource.Token);

        await action.Should().ThrowAsync<OperationCanceledException>();
        envelope.Status.Should().Be(ActionExecutionStatus.Failed);
        envelope.ProcessingStartedAtUtc.Should().BeNull();
        envelope.NextAttemptAt.Should().NotBeNull();
        envelope.ExecutionLogs.Should().ContainSingle(log =>
            log.ActionType == ActionExecutionLogType.Execute
            && log.Status == ActionExecutionStatus.Failed
            && log.ErrorMessage == "Command orchestration was cancelled.");
        _repository.UpdateCallCount.Should().Be(2);
        _unitOfWork.SaveCallCount.Should().Be(2);
    }

    [Test]
    public async Task OrchestrateAsync_CancelledDuringHandlerExecution_PropagatesAndRecordsRecoverableFailure()
    {
        var envelopeId = Id<CommandEnvelope>.New();
        var envelope = CreateEnvelope(envelopeId, new TestCommand { Value = "in-flight-cancelled" });
        _repository.AddEnvelope(envelope);
        var handlerStarted = new HandlerStartedSignal();
        var services = new ServiceCollection();
        services.AddSingleton(handlerStarted);
        services.AddScoped<IBackgroundAction<TestCommand>, CancellationAwareHandler>();
        services.AddSingleton<ILogger<BackgroundActionOrchestratorService>>(_ => new FakeLogger());
        _serviceProvider = services.BuildServiceProvider();
        var orchestrator = CreateOrchestrator();
        using var cancellationSource = new CancellationTokenSource();

        var orchestration = orchestrator.OrchestrateAsync(envelopeId, cancellationSource.Token);
        await handlerStarted.Started.Task.WaitAsync(TimeSpan.FromSeconds(5));
        cancellationSource.Cancel();

        await FluentActions.Invoking(async () => await orchestration)
            .Should().ThrowAsync<OperationCanceledException>();
        envelope.Status.Should().Be(ActionExecutionStatus.Failed);
        envelope.NextAttemptAt.Should().NotBeNull();
        envelope.ExecutionLogs.Should().ContainSingle(log =>
            log.ActionType == ActionExecutionLogType.Execute
            && log.Status == ActionExecutionStatus.Failed
            && log.ErrorMessage == "Command orchestration was cancelled.");
        _repository.UpdateTokens.Last().Should().Be(CancellationToken.None);
        _unitOfWork.SaveTokens.Last().Should().Be(CancellationToken.None);
    }

    [Test]
    public async Task OrchestrateAsync_AlreadyCompleted_SkipsDuplicate()
    {
        // Arrange
        var envelopeId = Id<CommandEnvelope>.New();
        var command = new TestCommand { Value = "test" };
        var envelope = CreateEnvelope(envelopeId, command);
        envelope.MarkCompleted();
        _repository.AddEnvelope(envelope);

        var services = new ServiceCollection();
        services.AddSingleton<ILogger<BackgroundActionOrchestratorService>>(_ => new FakeLogger());
        _serviceProvider = services.BuildServiceProvider();

        var orchestrator = CreateOrchestrator();
        var initialUpdateCount = _repository.UpdateCallCount;

        // Act
        await orchestrator.OrchestrateAsync(envelopeId);

        // Assert - Should not update envelope after initial load
        _repository.UpdateCallCount.Should().Be(initialUpdateCount);
    }

    [Test]
    public async Task OrchestrateAsync_InvalidCommandType_MarksDeadLettered()
    {
        // Arrange
        var envelopeId = Id<CommandEnvelope>.New();
        var envelope = new CommandEnvelope
        {
            Id = (LgymApi.Domain.ValueObjects.Id<CommandEnvelope>)envelopeId,
            CorrelationId = Id<CorrelationScope>.New(),
            PayloadJson = "{}",
            CommandTypeFullName = "NonExistent.FakeCommand",
            Status = ActionExecutionStatus.Pending
        };
        _repository.AddEnvelope(envelope);

        var services = new ServiceCollection();
        services.AddSingleton<ILogger<BackgroundActionOrchestratorService>>(_ => new FakeLogger());
        _serviceProvider = services.BuildServiceProvider();

        var orchestrator = CreateOrchestrator();

        // Act
        await orchestrator.OrchestrateAsync(envelopeId);

        // Assert
        envelope.Status.Should().Be(ActionExecutionStatus.DeadLettered);
        envelope.ExecutionLogs.Should().Contain(log =>
            log.ActionType == ActionExecutionLogType.DeadLetter
            && log.ErrorMessage == "Dead-lettered because command ID could not be resolved");
    }

    [Test]
    public async Task OrchestrateAsync_InvalidPayloadJson_MarksDeadLettered()
    {
        // Arrange
        var envelopeId = Id<CommandEnvelope>.New();
        var envelope = new CommandEnvelope
        {
            Id = (LgymApi.Domain.ValueObjects.Id<CommandEnvelope>)envelopeId,
            CorrelationId = Id<CorrelationScope>.New(),
            PayloadJson = "{invalid json",
            CommandTypeFullName = _commandContractRegistry.DescribeForWrite(typeof(TestCommand)).CanonicalId,
            Status = ActionExecutionStatus.Pending
        };
        _repository.AddEnvelope(envelope);

        var services = new ServiceCollection();
        services.AddSingleton<ILogger<BackgroundActionOrchestratorService>>(_ => new FakeLogger());
        _serviceProvider = services.BuildServiceProvider();

        var orchestrator = CreateOrchestrator();

        // Act
        await orchestrator.OrchestrateAsync(envelopeId);

        // Assert
        envelope.Status.Should().Be(ActionExecutionStatus.DeadLettered);
    }
    [Test]
    public async Task OrchestrateAsync_ScopeIsolation_EachHandlerGetsDistinctScopedDependency()
    {
        // Arrange
        var envelopeId = Id<CommandEnvelope>.New();
        var command = new TestCommand { Value = "scope-test" };
        var envelope = CreateEnvelope(envelopeId, command);
        _repository.AddEnvelope(envelope);

        var capturedScopeIds = new System.Collections.Concurrent.ConcurrentBag<string>();

        var services = new ServiceCollection();
        services.AddScoped<ScopedTracker>(); // Scoped service to track instance isolation
        services.AddScoped<IBackgroundAction<TestCommand>>(sp => new ScopeAwareHandler(sp.GetRequiredService<ScopedTracker>(), capturedScopeIds));
        services.AddScoped<IBackgroundAction<TestCommand>>(sp => new ScopeAwareHandler(sp.GetRequiredService<ScopedTracker>(), capturedScopeIds));
        services.AddSingleton<ILogger<BackgroundActionOrchestratorService>>(_ => new FakeLogger());
        _serviceProvider = services.BuildServiceProvider();

        var orchestrator = CreateOrchestrator();

        // Act
        await orchestrator.OrchestrateAsync(envelopeId);

        // Assert - Each handler should have seen a distinct ScopedTracker instance
        // Assert - Each handler execution should use distinct scoped instance
        // Note: Handlers may be resolved multiple times (count check + execution), so we verify distinct instances >= 2
        envelope.Status.Should().Be(ActionExecutionStatus.Completed);
        capturedScopeIds.Distinct().Count().Should().BeGreaterThanOrEqualTo(2, "At least 2 distinct scoped instances should exist (one per handler in isolated scope)");
    }

    [Test]
    public async Task OrchestrateAsync_ParallelismLimit_MaxFourConcurrentHandlers()
    {
        // Arrange
        var envelopeId = Id<CommandEnvelope>.New();
        var command = new TestCommand { Value = "concurrency-test" };
        var envelope = CreateEnvelope(envelopeId, command);
        _repository.AddEnvelope(envelope);

        var concurrencyTracker = new ConcurrencyTracker();

        var services = new ServiceCollection();
        // Register 8 handlers to exceed MaxDegreeOfParallelism=4
        for (int i = 0; i < 8; i++)
        {
            services.AddScoped<IBackgroundAction<TestCommand>>(_ => new ConcurrencyTestHandler(concurrencyTracker));
        }
        services.AddSingleton<ILogger<BackgroundActionOrchestratorService>>(_ => new FakeLogger());
        _serviceProvider = services.BuildServiceProvider();

        var orchestrator = CreateOrchestrator();

        // Act
        await orchestrator.OrchestrateAsync(envelopeId);

        // Assert - Max concurrent handlers never exceeds 4
        envelope.Status.Should().Be(ActionExecutionStatus.Completed);
        concurrencyTracker.MaxConcurrentExecutions.Should().BeLessThanOrEqualTo(4, "MaxDegreeOfParallelism=4 should be enforced");
        concurrencyTracker.TotalExecutions.Should().Be(8, "All 8 handlers should execute");
    }

    [Test]
    public async Task OrchestrateAsync_PerHandlerErrorTracking_DurableExecutionLogs()
    {
        // Arrange
        var envelopeId = Id<CommandEnvelope>.New();
        var command = new TestCommand { Value = "error-tracking" };
        var envelope = CreateEnvelope(envelopeId, command);
        _repository.AddEnvelope(envelope);

        var services = new ServiceCollection();
        services.AddScoped<IBackgroundAction<TestCommand>, TestActionHandlerSuccess>();
        services.AddScoped<IBackgroundAction<TestCommand>, TestActionHandlerFailure>();
        services.AddSingleton<ILogger<BackgroundActionOrchestratorService>>(_ => new FakeLogger());
        _serviceProvider = services.BuildServiceProvider();

        var orchestrator = CreateOrchestrator();

        // Act & Assert - Should throw because one handler failed (retry trigger)
        await FluentActions.Invoking(async () =>
            await orchestrator.OrchestrateAsync((Id<CommandEnvelope>)envelopeId)).Should().ThrowAsync<InvalidOperationException>();

        // Assert - Each handler execution should be tracked in ExecutionLogs
        envelope.Status.Should().Be(ActionExecutionStatus.Failed);
        var handlerLogs = envelope.ExecutionLogs.Where(log => log.ActionType == ActionExecutionLogType.HandlerExecution).ToList();
        handlerLogs.Count.Should().Be(2, "Both handlers should have ExecutionLog entries");
        handlerLogs.Count(log => log.Status == ActionExecutionStatus.Completed).Should().Be(1, "One handler succeeded");
        handlerLogs.Count(log => log.Status == ActionExecutionStatus.Failed).Should().Be(1, "One handler failed");
        handlerLogs.All(log => !string.IsNullOrWhiteSpace(log.HandlerTypeName)).Should().BeTrue("Each handler log should include concrete handler type");
        handlerLogs.Single(log => log.Status == ActionExecutionStatus.Failed).ErrorMessage.Should().NotBeNullOrEmpty("Failed handler should have error message");
    }


    [Test]
    public async Task OrchestrateAsync_ReadAliasResolution_ResolvesWithoutAssemblyQualifiedName()
    {
        // Arrange: Use FullName only (no assembly qualification)
        var envelopeId = Id<CommandEnvelope>.New();
        var command = new TestCommand { Value = "test" };
        var envelope = CreateEnvelope(envelopeId, command);
        // Override with FullName only (Type.FullName instead of AssemblyQualifiedName)
        envelope.CommandTypeFullName = typeof(TestCommand).FullName!;
        _repository.AddEnvelope(envelope);

        var services = new ServiceCollection();
        services.AddScoped<IBackgroundAction<TestCommand>, TestActionHandler1>();
        services.AddSingleton<ILogger<BackgroundActionOrchestratorService>>(_ => new FakeLogger());
        _serviceProvider = services.BuildServiceProvider();

        var orchestrator = CreateOrchestrator();

        // Act
        await orchestrator.OrchestrateAsync(envelopeId);

        // Assert: The read alias resolves through the closed registry.
        envelope.Status.Should().Be(ActionExecutionStatus.Completed, "FullName-only type resolution should succeed");
    }

    [Test]
    public async Task OrchestrateAsync_ReadAlias_LogsOnlyCanonicalCommandId()
    {
        var envelopeId = Id<CommandEnvelope>.New();
        var envelope = CreateEnvelope(envelopeId, new TestCommand { Value = "canonical-log" });
        envelope.CommandTypeFullName = typeof(TestCommand).FullName!;
        _repository.AddEnvelope(envelope);

        var services = new ServiceCollection();
        services.AddScoped<IBackgroundAction<TestCommand>, TestActionHandler1>();
        _serviceProvider = services.BuildServiceProvider();
        var logger = new FakeLogger();

        await CreateOrchestrator(logger).OrchestrateAsync(envelopeId);

        logger.Messages.Should().Contain(message =>
            message.Contains("Tests.BackgroundActionOrchestrator.TestCommand", StringComparison.Ordinal));
        logger.Messages.Should().NotContain(message =>
            message.Contains(typeof(TestCommand).FullName!, StringComparison.Ordinal));
    }

    [Test]
    public async Task OrchestrateAsync_AllHandlersSucceed_CreatesPerHandlerExecutionLogs()
    {
        // Arrange
        var envelopeId = Id<CommandEnvelope>.New();
        var command = new TestCommand { Value = "test" };
        var envelope = CreateEnvelope(envelopeId, command);
        _repository.AddEnvelope(envelope);

        var services = new ServiceCollection();
        services.AddScoped<IBackgroundAction<TestCommand>, TestActionHandler1>();
        services.AddScoped<IBackgroundAction<TestCommand>, TestActionHandler2>();
        services.AddSingleton<ILogger<BackgroundActionOrchestratorService>>(_ => new FakeLogger());
        _serviceProvider = services.BuildServiceProvider();

        var orchestrator = CreateOrchestrator();

        // Act
        await orchestrator.OrchestrateAsync(envelopeId);

        // Assert: Verify per-handler ExecutionLog entries exist for successful execution
        var handlerLogs = envelope.ExecutionLogs.Where(log => log.ActionType == ActionExecutionLogType.HandlerExecution).ToList();
        handlerLogs.Count.Should().Be(2, "Should create ExecutionLog for each handler");
        handlerLogs.All(log => log.Status == ActionExecutionStatus.Completed).Should().BeTrue("All handler logs should show Completed status");
        handlerLogs.All(log => !string.IsNullOrWhiteSpace(log.HandlerTypeName)).Should().BeTrue("Successful handler logs should include concrete handler type");
        handlerLogs.All(log => log.ErrorMessage == null).Should().BeTrue("Successful handlers should have no error message");
    }

    [Test]
    public async Task OrchestrateAsync_RetryableFailure_ThrowsExceptionToTriggerHangfireRetry()
    {
        // Arrange
        var envelopeId = Id<CommandEnvelope>.New();
        var command = new TestCommand { Value = "test" };
        var envelope = CreateEnvelope(envelopeId, command);
        _repository.AddEnvelope(envelope);

        var services = new ServiceCollection();
        services.AddScoped<IBackgroundAction<TestCommand>, TestActionHandlerFailure>();
        services.AddSingleton<ILogger<BackgroundActionOrchestratorService>>(_ => new FakeLogger());
        _serviceProvider = services.BuildServiceProvider();

        var orchestrator = CreateOrchestrator();

        // Act & Assert - Should throw to trigger Hangfire AutomaticRetry
        var ex = await FluentActions.Invoking(async () =>
            await orchestrator.OrchestrateAsync((Id<CommandEnvelope>)envelopeId)).Should().ThrowAsync<InvalidOperationException>();

        ex.Which.Message.Should().Contain("Retry scheduled", "Exception message should indicate retry scheduled");
        envelope.Status.Should().Be(ActionExecutionStatus.Failed, "Should be in Failed state");
        envelope.NextAttemptAt.Should().NotBeNull("NextAttemptAt should be scheduled");
        envelope.NextAttemptAt.Value.Should().BeAfter(DateTimeOffset.UtcNow, "NextAttemptAt should be in future (backoff delay)");
    }

    [Test]
    public async Task OrchestrateAsync_RetryableFailure_SecondAttemptAlsoThrows()
    {
        // Arrange
        var envelopeId = Id<CommandEnvelope>.New();
        var command = new TestCommand { Value = "test" };
        var envelope = CreateEnvelope(envelopeId, command);
        // Simulate first failure already recorded.
        AddExecuteAttemptLog(envelope, 0, ActionExecutionStatus.Failed, "First attempt failed");
        envelope.RecordAttemptFailure("First attempt failed");
        _repository.AddEnvelope(envelope);

        var services = new ServiceCollection();
        services.AddScoped<IBackgroundAction<TestCommand>, TestActionHandlerFailure>();
        services.AddSingleton<ILogger<BackgroundActionOrchestratorService>>(_ => new FakeLogger());
        _serviceProvider = services.BuildServiceProvider();

        var orchestrator = CreateOrchestrator();

        // Act & Assert - Second attempt should also throw (ShouldRetry still true)
        var ex = await FluentActions.Invoking(async () =>
            await orchestrator.OrchestrateAsync((Id<CommandEnvelope>)envelopeId)).Should().ThrowAsync<InvalidOperationException>();

        ex.Which.Message.Should().Contain("Retry scheduled", "Exception should indicate retry");
        envelope.Status.Should().Be(ActionExecutionStatus.Failed);
        envelope.ExecutionLogs.Count(log => log.ActionType == ActionExecutionLogType.Execute).Should().Be(2, "Should have 2 execution attempts");
    }

    [Test]
    public async Task OrchestrateAsync_AlreadyProcessing_SkipsDuplicateRedelivery()
    {
        // Arrange
        var envelopeId = Id<CommandEnvelope>.New();
        var command = new TestCommand { Value = "duplicate-redelivery" };
        var envelope = CreateEnvelope(envelopeId, command);
        envelope.Status = ActionExecutionStatus.Processing;
        envelope.LastAttemptAt = DateTimeOffset.UtcNow;
        _repository.AddEnvelope(envelope);

        var services = new ServiceCollection();
        services.AddScoped<IBackgroundAction<TestCommand>, TestActionHandlerFailure>();
        services.AddSingleton<ILogger<BackgroundActionOrchestratorService>>(_ => new FakeLogger());
        _serviceProvider = services.BuildServiceProvider();

        var orchestrator = CreateOrchestrator();

        // Act
        await orchestrator.OrchestrateAsync(envelopeId);

        // Assert
        envelope.Status.Should().Be(ActionExecutionStatus.Processing);
        envelope.ExecutionLogs.Should().BeEmpty();
        _repository.UpdateCallCount.Should().Be(0);
    }

    [Test]
    public async Task OrchestrateAsync_ErrorContextDurability_StoresFullExceptionStackTrace()
    {
        // Arrange
        var envelopeId = Id<CommandEnvelope>.New();
        var command = new TestCommand { Value = "error-context" };
        var envelope = CreateEnvelope(envelopeId, command);
        _repository.AddEnvelope(envelope);

        var services = new ServiceCollection();
        services.AddScoped<IBackgroundAction<TestCommand>, TestActionHandlerFailure>();
        services.AddSingleton<ILogger<BackgroundActionOrchestratorService>>(_ => new FakeLogger());
        _serviceProvider = services.BuildServiceProvider();

        var orchestrator = CreateOrchestrator();

        // Act & Assert - Should throw to trigger retry
        await FluentActions.Invoking(async () =>
            await orchestrator.OrchestrateAsync((Id<CommandEnvelope>)envelopeId)).Should().ThrowAsync<InvalidOperationException>();

        // Assert - ExecutionLog should contain full exception details
        var handlerLogs = envelope.ExecutionLogs.Where(log => log.ActionType == ActionExecutionLogType.HandlerExecution).ToList();
        handlerLogs.Count.Should().Be(1, "Should have one handler execution log");

        var failedLog = handlerLogs.Single();
        failedLog.Status.Should().Be(ActionExecutionStatus.Failed);
        failedLog.ErrorMessage.Should().NotBeNullOrEmpty("Should have error message");
        failedLog.ErrorDetails.Should().NotBeNullOrEmpty("Should have error details");
        failedLog.ErrorDetails.Should().Contain("InvalidOperationException", "Should contain exception type");
        failedLog.ErrorDetails.Should().Contain("Test handler failure", "Should contain exception message");
        failedLog.ErrorDetails.Should().Contain("at ", "Should contain stack trace");
    }

    private BackgroundActionOrchestratorService CreateOrchestrator(FakeLogger? logger = null)
    {
        return new BackgroundActionOrchestratorService(
            new BackgroundActionResolver(_serviceProvider.GetRequiredService<IServiceScopeFactory>()),
            _commandContractRegistry,
            _repository,
            logger ?? new FakeLogger());
    }

    private CommandEnvelope CreateEnvelope<TCommand>(Id<CommandEnvelope> envelopeId, TCommand command)
        where TCommand : ApplicationActionCommand
    {
        return new CommandEnvelope
        {
            Id = envelopeId,
            CorrelationId = Id<CorrelationScope>.New(),
            PayloadJson = JsonSerializer.Serialize(command, SharedSerializationOptions.Current),
            CommandTypeFullName = _commandContractRegistry.DescribeForWrite(typeof(TCommand)).CanonicalId,
            Status = ActionExecutionStatus.Pending
        };
    }

    private static void AddExecuteAttemptLog(CommandEnvelope envelope, int attemptNumber, ActionExecutionStatus status, string? errorMessage = null)
    {
        envelope.ExecutionLogs.Add(new ActionExecutionLog
        {
            CommandEnvelopeId = envelope.Id,
            ActionType = ActionExecutionLogType.Execute,
            Status = status,
            AttemptNumber = attemptNumber,
            ErrorMessage = errorMessage
        });
    }

    // Test command and handlers
    private class TestCommand : ApplicationActionCommand
    {
        public string Value { get; set; } = string.Empty;
    }

    private class DerivedTestCommand : TestCommand
    {
        public int Extra { get; set; }
    }

    private sealed class DerivedTestActionHandler : IBackgroundAction<DerivedTestCommand>
    {
        public Task ExecuteAsync(DerivedTestCommand command, CancellationToken cancellationToken = default) =>
            Task.CompletedTask;
    }

    private class TestActionHandler1 : IBackgroundAction<TestCommand>
    {
        public Task ExecuteAsync(TestCommand command, CancellationToken cancellationToken = default)
        {
            return Task.CompletedTask;
        }
    }

    private class TestActionHandler2 : IBackgroundAction<TestCommand>
    {
        public Task ExecuteAsync(TestCommand command, CancellationToken cancellationToken = default)
        {
            return Task.CompletedTask;
        }
    }

    private class TestActionHandlerSuccess : IBackgroundAction<TestCommand>
    {
        public Task ExecuteAsync(TestCommand command, CancellationToken cancellationToken = default)
        {
            return Task.CompletedTask;
        }
    }

    private class TestActionHandlerFailure : IBackgroundAction<TestCommand>
    {
        public Task ExecuteAsync(TestCommand command, CancellationToken cancellationToken = default)
        {
            throw new InvalidOperationException("Test handler failure");
        }
    }

    // Test helpers for scope isolation and concurrency verification

    private sealed class ScopedTracker
    {
        public string InstanceId { get; } = $"{Id<ScopedTracker>.New():N}";
    }

    private sealed class ConcurrencyTracker
    {
        private int _currentConcurrent = 0;
        private int _maxConcurrent = 0;
        private int _totalExecutions = 0;
        private readonly object _lock = new object();

        public int MaxConcurrentExecutions => _maxConcurrent;
        public int TotalExecutions => _totalExecutions;

        public async Task TrackExecutionAsync()
        {
            lock (_lock)
            {
                _currentConcurrent++;
                _totalExecutions++;
                if (_currentConcurrent > _maxConcurrent)
                {
                    _maxConcurrent = _currentConcurrent;
                }
            }

            // Simulate work to increase concurrency pressure
            await Task.Delay(50);

            lock (_lock)
            {
                _currentConcurrent--;
            }
        }
    }


    private sealed class ScopeAwareHandler : IBackgroundAction<TestCommand>
    {
        private readonly ScopedTracker _tracker;
        private readonly System.Collections.Concurrent.ConcurrentBag<string> _capturedIds;

        public ScopeAwareHandler(ScopedTracker tracker, System.Collections.Concurrent.ConcurrentBag<string> capturedIds)
        {
            _tracker = tracker;
            _capturedIds = capturedIds;
            _capturedIds.Add(_tracker.InstanceId);
        }

        public Task ExecuteAsync(TestCommand command, CancellationToken cancellationToken = default)
        {
            return Task.CompletedTask;
        }
    }
    private sealed class ConcurrencyTestHandler : IBackgroundAction<TestCommand>
    {
        private readonly ConcurrencyTracker _tracker;

        public ConcurrencyTestHandler(ConcurrencyTracker tracker)
        {
            _tracker = tracker;
        }

        public async Task ExecuteAsync(TestCommand command, CancellationToken cancellationToken = default)
        {
            await _tracker.TrackExecutionAsync();
        }
    }

    private sealed class HandlerStartedSignal
    {
        public TaskCompletionSource Started { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
    }

    private sealed class CancellationAwareHandler : IBackgroundAction<TestCommand>
    {
        private readonly HandlerStartedSignal _handlerStarted;

        public CancellationAwareHandler(HandlerStartedSignal handlerStarted)
        {
            _handlerStarted = handlerStarted;
        }

        public async Task ExecuteAsync(TestCommand command, CancellationToken cancellationToken = default)
        {
            _handlerStarted.Started.SetResult();
            await Task.Delay(Timeout.InfiniteTimeSpan, cancellationToken);
        }
    }

    // Fake implementations for testing
    private sealed class FakeCommandEnvelopeRepository : ICommandEnvelopeRepository, ICommandEnvelopeRuntime
    {
        private readonly Dictionary<Id<CommandEnvelope>, CommandEnvelope> _envelopes = new();
        public FakeUnitOfWork UnitOfWork { private get; set; } = null!;
        public int UpdateCallCount { get; private set; }
        public List<CancellationToken> UpdateTokens { get; } = [];

        public void AddEnvelope(CommandEnvelope envelope)
        {
            _envelopes[envelope.Id] = envelope;
        }

        public Task AddAsync(CommandEnvelope envelope, CancellationToken cancellationToken = default)
        {
            _envelopes[envelope.Id] = envelope;
            return Task.CompletedTask;
        }

        public Task<CommandEnvelope?> FindByIdAsync(Id<CommandEnvelope> id, CancellationToken cancellationToken = default)
        {
            _envelopes.TryGetValue(id, out var envelope);
            return Task.FromResult(envelope);
        }

        public Task<CommandEnvelope?> FindByCorrelationIdAsync(Id<CorrelationScope> correlationId, CancellationToken cancellationToken = default)
        {
            return Task.FromResult(_envelopes.Values.FirstOrDefault(e => e.CorrelationId == correlationId));
        }

        public Task<List<CommandEnvelope>> GetPendingRetriesAsync(CancellationToken cancellationToken = default)
        {
            return Task.FromResult(_envelopes.Values
                .Where(e => e.Status == ActionExecutionStatus.Failed && e.NextAttemptAt <= DateTimeOffset.UtcNow)
                .ToList());
        }

        public Task UpdateAsync(CommandEnvelope envelope, CancellationToken cancellationToken = default)
        {
            UpdateCallCount++;
            UpdateTokens.Add(cancellationToken);
            return Task.CompletedTask;
        }

        public void Detach(CommandEnvelope envelope) { }

        public Task<CommandEnvelope> AddOrGetExistingAsync(CommandEnvelope envelope, CancellationToken cancellationToken = default)
        {
            var existing = _envelopes.Values.FirstOrDefault(e => e.CorrelationId == envelope.CorrelationId);
            if (existing != null)
            {
                return Task.FromResult(existing);
            }

            _envelopes[envelope.Id] = envelope;
            return Task.FromResult(envelope);
        }

        public Task<CommandEnvelopeReceipt> PersistAsync(CommandEnvelopeRequest request, CancellationToken cancellationToken = default) => throw new NotSupportedException();

        public Task<CommandEnvelopeReceipt> StageAsync(CommandEnvelopeRequest request, CancellationToken cancellationToken = default) => throw new NotSupportedException();

        public async Task<CommandEnvelopeStart> BeginAsync(string envelopeId, CancellationToken cancellationToken = default)
        {
            if (!Id<CommandEnvelope>.TryParse(envelopeId, out var id))
            {
                throw new ArgumentException("Command envelope ID is invalid.", nameof(envelopeId));
            }

            var envelope = await FindByIdAsync(id, cancellationToken);
            if (envelope is null)
            {
                return new CommandEnvelopeStart("not-found", null, null, 0);
            }

            if (envelope.Status is ActionExecutionStatus.Completed or ActionExecutionStatus.DeadLettered or ActionExecutionStatus.Processing)
            {
                return new CommandEnvelopeStart(envelope.Status.ToString().ToLowerInvariant(), null, null, 0);
            }

            var attemptNumber = envelope.GetExecutionAttemptCount();
            envelope.Status = ActionExecutionStatus.Processing;
            envelope.LastAttemptAt = DateTimeOffset.UtcNow;
            envelope.ProcessingStartedAtUtc = DateTimeOffset.UtcNow;
            envelope.NextAttemptAt = null;
            envelope.ExecutionLogs.Add(new ActionExecutionLog
            {
                Id = Id<ActionExecutionLog>.New(),
                CommandEnvelopeId = envelope.Id,
                ActionType = ActionExecutionLogType.Execute,
                Status = ActionExecutionStatus.Processing,
                AttemptNumber = attemptNumber
            });
            await UpdateAsync(envelope, cancellationToken);
            await UnitOfWork.SaveChangesAsync(cancellationToken);
            return new CommandEnvelopeStart("running", envelope.CommandTypeFullName, envelope.PayloadJson, attemptNumber);
        }

        public async Task<CommandEnvelopeFinalization> FinalizeAsync(
            string envelopeId,
            int attemptNumber,
            IReadOnlyList<CommandHandlerResult> results,
            CancellationToken cancellationToken = default)
        {
            var envelope = await FindRequiredAsync(envelopeId, cancellationToken);
            var attemptLog = envelope.ExecutionLogs.Last(log => log.ActionType == ActionExecutionLogType.Execute && log.AttemptNumber == attemptNumber);
            foreach (var result in results)
            {
                envelope.ExecutionLogs.Add(new ActionExecutionLog
                {
                    Id = Id<ActionExecutionLog>.New(),
                    CommandEnvelopeId = envelope.Id,
                    ActionType = ActionExecutionLogType.HandlerExecution,
                    Status = result.Success ? ActionExecutionStatus.Completed : ActionExecutionStatus.Failed,
                    AttemptNumber = attemptNumber,
                    HandlerTypeName = result.HandlerTypeName,
                    ErrorMessage = result.ErrorMessage,
                    ErrorDetails = result.ErrorDetails
                });
            }

            var failures = results.Where(result => !result.Success).ToList();
            if (failures.Count == 0)
            {
                attemptLog.Status = ActionExecutionStatus.Completed;
                envelope.MarkCompleted();
                await UpdateAsync(envelope, cancellationToken);
                await UnitOfWork.SaveChangesAsync(cancellationToken);
                return new CommandEnvelopeFinalization(false, null, false, null);
            }

            var errorMessage = string.Join("; ", failures.Select(result => result.ErrorMessage));
            var errorDetails = string.Join(Environment.NewLine + Environment.NewLine, failures
                .Where(result => !string.IsNullOrWhiteSpace(result.ErrorDetails))
                .Select(result => result.ErrorDetails));
            attemptLog.Status = ActionExecutionStatus.Failed;
            attemptLog.ErrorMessage = errorMessage;
            attemptLog.ErrorDetails = string.IsNullOrWhiteSpace(errorDetails) ? null : errorDetails;
            envelope.RecordAttemptFailure(errorMessage);
            await UpdateAsync(envelope, cancellationToken);
            await UnitOfWork.SaveChangesAsync(cancellationToken);
            if (envelope.ShouldRetry())
            {
                return new CommandEnvelopeFinalization(true, envelope.NextAttemptAt?.ToString("O"), false, errorMessage);
            }

            envelope.MarkDeadLettered("Dead-lettered after maximum retry attempts exceeded", string.IsNullOrWhiteSpace(errorDetails) ? errorMessage : errorDetails);
            await UpdateAsync(envelope, cancellationToken);
            await UnitOfWork.SaveChangesAsync(cancellationToken);
            return new CommandEnvelopeFinalization(false, null, true, errorMessage);
        }

        public async Task RecordFaultAsync(string envelopeId, string reason, string errorMessage, string errorDetails, CancellationToken cancellationToken = default)
        {
            var envelope = await FindRequiredAsync(envelopeId, cancellationToken);
            var attemptLog = envelope.ExecutionLogs.Last(log => log.ActionType == ActionExecutionLogType.Execute && log.Status == ActionExecutionStatus.Processing);
            attemptLog.Status = ActionExecutionStatus.Failed;
            attemptLog.ErrorMessage = errorMessage;
            attemptLog.ErrorDetails = errorDetails;
            envelope.MarkDeadLettered(reason, errorDetails);
            await UpdateAsync(envelope, cancellationToken);
            await UnitOfWork.SaveChangesAsync(cancellationToken);
        }

        public async Task RecordCancellationAsync(string envelopeId)
        {
            var envelope = await FindRequiredAsync(envelopeId, CancellationToken.None);
            const string message = "Command orchestration was cancelled.";
            var attemptLog = envelope.ExecutionLogs.Last(log => log.ActionType == ActionExecutionLogType.Execute && log.Status == ActionExecutionStatus.Processing);
            attemptLog.Status = ActionExecutionStatus.Failed;
            attemptLog.ErrorMessage = message;
            attemptLog.ErrorDetails = message;
            envelope.RecordAttemptFailure(message, message);
            await UpdateAsync(envelope, CancellationToken.None);
            await UnitOfWork.SaveChangesAsync(CancellationToken.None);
        }

        private async Task<CommandEnvelope> FindRequiredAsync(string envelopeId, CancellationToken cancellationToken)
        {
            if (!Id<CommandEnvelope>.TryParse(envelopeId, out var id))
            {
                throw new ArgumentException("Command envelope ID is invalid.", nameof(envelopeId));
            }

            return await FindByIdAsync(id, cancellationToken)
                ?? throw new InvalidOperationException($"Command envelope {envelopeId} was not found.");
        }

        public Task<List<CommandEnvelope>> GetPendingUndispatchedAsync(CancellationToken cancellationToken = default)
            => Task.FromResult(_envelopes.Values.Where(e => e.Status == ActionExecutionStatus.Pending && e.DispatchedAt == null).ToList());

        public Task<List<CommandEnvelope>> GetFailedAsync(CancellationToken cancellationToken = default)
            => Task.FromResult(_envelopes.Values.Where(e => e.Status == ActionExecutionStatus.Failed).ToList());

        public Task<List<CommandEnvelope>> GetDeadLetteredAsync(CancellationToken cancellationToken = default)
            => Task.FromResult(_envelopes.Values.Where(e => e.Status == ActionExecutionStatus.DeadLettered).ToList());

        public Task<int> CountByStatusAsync(ActionExecutionStatus status, CancellationToken cancellationToken = default)
            => Task.FromResult(_envelopes.Values.Count(e => e.Status == status));

        public Task<int> DeleteCompletedOlderThanAsync(DateTimeOffset cutoffDate, CancellationToken cancellationToken = default)
        {
            var toDelete = _envelopes.Values
                .Where(e => e.Status == ActionExecutionStatus.Completed && e.CompletedAt.HasValue && e.CompletedAt < cutoffDate)
                .ToList();

            var count = toDelete.Count;
            foreach (var e in toDelete)
            {
                _envelopes.Remove(e.Id);
            }

            return Task.FromResult(count);
        }
    }

    private sealed class FakeUnitOfWork : IUnitOfWork
    {
        public int SaveCallCount { get; private set; }
        public List<CancellationToken> SaveTokens { get; } = [];

        public Task<int> SaveChangesAsync(CancellationToken cancellationToken = default)
        {
            SaveCallCount++;
            SaveTokens.Add(cancellationToken);
            return Task.FromResult(1);
        }

        public Task<IUnitOfWorkTransaction> BeginTransactionAsync(CancellationToken cancellationToken = default)
            => throw new NotSupportedException();

        public void DetachEntity<TEntity>(TEntity entity) where TEntity : class { }
    }

    private sealed class FakeTransaction : IUnitOfWorkTransaction
    {
        public Task CommitAsync(CancellationToken cancellationToken = default) => Task.CompletedTask;
        public Task RollbackAsync(CancellationToken cancellationToken = default) => Task.CompletedTask;
        public ValueTask DisposeAsync() => ValueTask.CompletedTask;
    }

    private sealed class FakeLogger : ILogger<BackgroundActionOrchestratorService>
    {
        public List<string> Messages { get; } = new();

        public IDisposable? BeginScope<TState>(TState state) where TState : notnull => null;
        public bool IsEnabled(LogLevel logLevel) => true;
        public void Log<TState>(LogLevel logLevel, EventId eventId, TState state, Exception? exception, Func<TState, Exception?, string> formatter) =>
            Messages.Add(formatter(state, exception));
    }
}






