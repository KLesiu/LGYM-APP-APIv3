using FluentAssertions;
using System.Security.Cryptography;
using System.Text;
using LgymApi.Domain.ValueObjects;
using LgymApi.Application.Repositories;
using LgymApi.Application.Platform.Contracts.BackgroundCommands;
using LgymApi.BackgroundWorker;
using LgymApi.BackgroundWorker.Actions.Contracts;
using LgymApi.BackgroundWorker.Runtime;
using LgymApi.Domain.Entities;
using LgymApi.Domain.Enums;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using NUnit.Framework;
using IActionMessageScheduler = LgymApi.BackgroundWorker.Common.IActionMessageScheduler;

namespace LgymApi.UnitTests;

/// <summary>
/// Tests for CommandDispatcher covering:
/// - Single handler staging flow
/// - Multi-handler staging flow
/// - Zero-handler no-persistence path
/// - Duplicate/idempotent path blocks duplicate staging
/// - Durable-envelope persistence assertions
/// </summary>
[TestFixture]
public sealed class BackgroundActionDispatcherTests
{
    private FakeCommandEnvelopeRepository _repository = null!;
    private FakeUnitOfWork _unitOfWork = null!;
    private CommandContractRegistry _commandContractRegistry = null!;
    private Microsoft.Extensions.DependencyInjection.ServiceProvider _serviceProvider = null!;

    [SetUp]
    public void SetUp()
    {
        _repository = new FakeCommandEnvelopeRepository();
        _unitOfWork = new FakeUnitOfWork();
        _repository.UnitOfWork = _unitOfWork;
        _commandContractRegistry = CommandContractRegistry.CreateForTesting(
        [
            new CommandContract(
                "Tests.BackgroundActionDispatcher.TestCommand",
                typeof(TestCommand),
                typeof(TestCommand).FullName!,
                [typeof(TestActionHandler1), typeof(TestActionHandler2), typeof(TestActionHandlerSuccess)])
        ]);
    }

    [TearDown]
    public void TearDown()
    {
        _serviceProvider?.Dispose();
    }

    [Test]
    public async Task Enqueue_SingleHandler_CreatesEnvelope()
    {
        // Arrange
        var services = new ServiceCollection();
        services.AddScoped<IBackgroundAction<TestCommand>, TestActionHandler1>();
        services.AddSingleton<ILogger<CommandDispatcher>>(_ => new FakeLogger<CommandDispatcher>());
        _serviceProvider = services.BuildServiceProvider();

        var dispatcher = CreateDispatcher();
        var command = new TestCommand { Value = "test-single" };

        // Act
        await dispatcher.EnqueueAsync(command);

        // Assert
        _repository.Envelopes.Count.Should().Be(1, "Should persist exactly one envelope");

        var envelope = _repository.Envelopes.First();
        envelope.Status.Should().Be(ActionExecutionStatus.Pending);
        envelope.CommandTypeFullName.Should().Be("Tests.BackgroundActionDispatcher.TestCommand");
        envelope.PayloadJson.Should().Contain("test-single");
    }

    [Test]
    public async Task Enqueue_MultipleHandlers_CreatesOneEnvelope()
    {
        // Arrange
        var services = new ServiceCollection();
        services.AddScoped<IBackgroundAction<TestCommand>, TestActionHandler1>();
        services.AddScoped<IBackgroundAction<TestCommand>, TestActionHandler2>();
        services.AddScoped<IBackgroundAction<TestCommand>, TestActionHandlerSuccess>();
        services.AddSingleton<ILogger<CommandDispatcher>>(_ => new FakeLogger<CommandDispatcher>());
        _serviceProvider = services.BuildServiceProvider();

        var dispatcher = CreateDispatcher();
        var command = new TestCommand { Value = "test-multi" };

        // Act
        await dispatcher.EnqueueAsync(command);

        // Assert
        _repository.Envelopes.Count.Should().Be(1, "Should persist exactly one envelope despite multiple handlers");
    }

    [Test]
    public async Task Enqueue_ZeroHandlers_NoPersistenceAndNoFailure()
    {
        // Arrange
        var services = new ServiceCollection();
        services.AddSingleton<ILogger<CommandDispatcher>>(_ => new FakeLogger<CommandDispatcher>());
        _serviceProvider = services.BuildServiceProvider(); // No handlers registered

        var dispatcher = CreateDispatcher();
        var command = new TestCommand { Value = "test-zero" };

        // Act
        await dispatcher.EnqueueAsync(command);

        // Assert - Zero-handler path: safe no-op, no failure, no enqueue
        _repository.Envelopes.Count.Should().Be(0, "Should not persist envelope when no handlers registered");
        _unitOfWork.SaveCallCount.Should().Be(0);
    }

    [Test]
    public async Task Enqueue_DuplicateCommand_BlocksDuplicatePersistence()
    {
        // Arrange
        var services = new ServiceCollection();
        services.AddScoped<IBackgroundAction<TestCommand>, TestActionHandler1>();
        services.AddSingleton<ILogger<CommandDispatcher>>(_ => new FakeLogger<CommandDispatcher>());
        _serviceProvider = services.BuildServiceProvider();

        var dispatcher = CreateDispatcher();
        var command = new TestCommand { Value = "test-duplicate" };

        // Act - Dispatch same command twice
        await dispatcher.EnqueueAsync(command);
        await dispatcher.EnqueueAsync(command); // Duplicate dispatch with identical content

        // Assert - Deterministic correlation ID should deduplicate second dispatch

        // Assert - Idempotency check: duplicate envelope should not enqueue
        _repository.Envelopes.Count.Should().Be(1, "Should not create duplicate envelope for identical command");
    }

    [Test]
    public async Task Enqueue_NullCommand_ThrowsArgumentNullException()
    {
        // Arrange
        var services = new ServiceCollection();
        services.AddSingleton<ILogger<CommandDispatcher>>(_ => new FakeLogger<CommandDispatcher>());
        _serviceProvider = services.BuildServiceProvider();

        var dispatcher = CreateDispatcher();

        // Act & Assert
        var action = async () => await dispatcher.EnqueueAsync<TestCommand>(null!);
        await action.Should().ThrowAsync<ArgumentNullException>();
    }

    [Test]
    public async Task Enqueue_ExactTypeMatchingOnly_DerivedCommandDoesNotInherit()
    {
        // Arrange
        var services = new ServiceCollection();
        services.AddScoped<IBackgroundAction<TestCommand>, TestActionHandler1>(); // Base command handler
        services.AddSingleton<ILogger<CommandDispatcher>>(_ => new FakeLogger<CommandDispatcher>());
        _serviceProvider = services.BuildServiceProvider();

        var dispatcher = CreateDispatcher();
        var derivedCommand = new DerivedTestCommand { Value = "derived", DerivedValue = "extra" };

        // Act - DerivedTestCommand has NO handlers registered (base handler should NOT match)
        await dispatcher.EnqueueAsync(derivedCommand);

        // Assert - Zero-handler path because exact-type matching only
        _repository.Envelopes.Count.Should().Be(0, "Should not persist envelope for derived command when only base handler exists");
        _unitOfWork.SaveCallCount.Should().Be(0);
    }

    [Test]
    public async Task Enqueue_RegisteredHandlerForUnregisteredCommand_ThrowsBeforePersistence()
    {
        var services = new ServiceCollection();
        services.AddScoped<IBackgroundAction<UnregisteredCommand>, UnregisteredCommandHandler>();
        services.AddSingleton<ILogger<CommandDispatcher>>(_ => new FakeLogger<CommandDispatcher>());
        _serviceProvider = services.BuildServiceProvider();

        var dispatcher = CreateDispatcher();
        var action = () => dispatcher.EnqueueAsync(new UnregisteredCommand());

        await action.Should().ThrowAsync<InvalidOperationException>()
            .WithMessage("*absent from the closed command registry*");
        _repository.Envelopes.Should().BeEmpty();
        _unitOfWork.SaveCallCount.Should().Be(0);
    }

    [Test]
    public async Task Enqueue_CanonicalNameCollidingRuntimeType_ThrowsBeforePersistence()
    {
        var collisionRegistry = CommandContractRegistry.CreateForTesting(
        [
            new CommandContract(
                typeof(CanonicalNameCollisionCommand).FullName!,
                typeof(TestCommand),
                typeof(TestCommand).FullName!,
                [typeof(TestActionHandler1)])
        ]);
        var services = new ServiceCollection();
        services.AddScoped<IBackgroundAction<CanonicalNameCollisionCommand>, CanonicalNameCollisionHandler>();
        services.AddSingleton<ILogger<CommandDispatcher>>(_ => new FakeLogger<CommandDispatcher>());
        _serviceProvider = services.BuildServiceProvider();
        var dispatcher = new CommandDispatcher(
            new BackgroundActionResolver(_serviceProvider.GetRequiredService<IServiceScopeFactory>()),
            collisionRegistry,
            _repository,
            _serviceProvider.GetRequiredService<ILogger<CommandDispatcher>>());

        var action = () => dispatcher.EnqueueAsync(new CanonicalNameCollisionCommand());

        await action.Should().ThrowAsync<InvalidOperationException>()
            .WithMessage("*absent from the closed command registry*");
        _repository.Envelopes.Should().BeEmpty();
        _unitOfWork.SaveCallCount.Should().Be(0);
    }

    [Test]
    public async Task Enqueue_PersistsEnvelope()
    {
        // Arrange
        var services = new ServiceCollection();
        services.AddScoped<IBackgroundAction<TestCommand>, TestActionHandler1>();
        services.AddSingleton<ILogger<CommandDispatcher>>(_ => new FakeLogger<CommandDispatcher>());
        _serviceProvider = services.BuildServiceProvider();

        var dispatcher = CreateDispatcher();
        var command = new TestCommand { Value = "test-order" };

        // Act
        await dispatcher.EnqueueAsync(command);

        // Assert - envelope is persisted for the committed-intent dispatcher.
        _repository.Envelopes.Count.Should().Be(1);
    }

    [Test]
    public async Task Enqueue_RegisteredCommand_LogsOnlyCanonicalCommandId()
    {
        var logger = new FakeLogger<CommandDispatcher>();
        var services = new ServiceCollection();
        services.AddScoped<IBackgroundAction<TestCommand>, TestActionHandler1>();
        services.AddSingleton<ILogger<CommandDispatcher>>(logger);
        _serviceProvider = services.BuildServiceProvider();

        await CreateDispatcher().EnqueueAsync(new TestCommand { Value = "canonical-log" });

        logger.Messages.Should().Contain(message =>
            message.Contains("Tests.BackgroundActionDispatcher.TestCommand", StringComparison.Ordinal));
        logger.Messages.Should().NotContain(message =>
            message.Contains(typeof(TestCommand).FullName!, StringComparison.Ordinal));
    }

    private CommandDispatcher CreateDispatcher()
    {
        return new CommandDispatcher(
            new BackgroundActionResolver(_serviceProvider.GetRequiredService<IServiceScopeFactory>()),
            _commandContractRegistry,
            _repository,
            _serviceProvider.GetRequiredService<ILogger<CommandDispatcher>>());
    }

    // Test doubles
    private sealed class FakeCommandEnvelopeRepository : ICommandEnvelopeRepository, ICommandEnvelopeRuntime
    {
        public List<CommandEnvelope> Envelopes { get; } = new();
        public FakeUnitOfWork UnitOfWork { private get; set; } = null!;

        public void AddEnvelope(CommandEnvelope envelope) => Envelopes.Add(envelope);

        public Task AddAsync(CommandEnvelope envelope, CancellationToken cancellationToken = default)
        {
            Envelopes.Add(envelope);
            return Task.CompletedTask;
        }

        public Task<CommandEnvelope?> FindByIdAsync(Id<CommandEnvelope> id, CancellationToken cancellationToken = default)
        {
            return Task.FromResult(Envelopes.FirstOrDefault(e => e.Id == id));
        }

        public Task<CommandEnvelope?> FindByCorrelationIdAsync(Id<CorrelationScope> correlationId, CancellationToken cancellationToken = default)
        {
            return Task.FromResult(Envelopes.FirstOrDefault(e => e.CorrelationId == correlationId));
        }

        public void Detach(CommandEnvelope envelope) { }

        public Task<List<CommandEnvelope>> GetPendingRetriesAsync(CancellationToken cancellationToken = default)
        {
            return Task.FromResult(Envelopes
                .Where(e => e.Status == ActionExecutionStatus.Failed && e.NextAttemptAt <= DateTimeOffset.UtcNow)
                .ToList());
        }

        public Task UpdateAsync(CommandEnvelope envelope, CancellationToken cancellationToken = default)
        {
            return Task.CompletedTask;
        }

        public Task<CommandEnvelope> AddOrGetExistingAsync(CommandEnvelope envelope, CancellationToken cancellationToken = default)
        {

            var existing = Envelopes.FirstOrDefault(e => e.CorrelationId == envelope.CorrelationId);
            if (existing != null)
            {
                return Task.FromResult(existing);
            }

            Envelopes.Add(envelope);
            return Task.FromResult(envelope);
        }

        public async Task<CommandEnvelopeReceipt> PersistAsync(CommandEnvelopeRequest request, CancellationToken cancellationToken = default)
        {
            var envelope = CreateEnvelope(request);
            var winner = await AddOrGetExistingAsync(envelope, cancellationToken);
            if (!ReferenceEquals(winner, envelope))
            {
                return new CommandEnvelopeReceipt(winner.Id.ToString(), true);
            }

            await UnitOfWork.SaveChangesAsync(cancellationToken);
            return new CommandEnvelopeReceipt(envelope.Id.ToString(), false);
        }

        public async Task<CommandEnvelopeReceipt> StageAsync(CommandEnvelopeRequest request, CancellationToken cancellationToken = default)
        {
            var envelope = CreateEnvelope(request);
            var winner = await AddOrGetExistingAsync(envelope, cancellationToken);
            return new CommandEnvelopeReceipt(winner.Id.ToString(), !ReferenceEquals(winner, envelope));
        }

        public Task<CommandEnvelopeStart> BeginAsync(string envelopeId, CancellationToken cancellationToken = default) => throw new NotSupportedException();

        public Task<CommandEnvelopeFinalization> FinalizeAsync(string envelopeId, int attemptNumber, IReadOnlyList<CommandHandlerResult> results, CancellationToken cancellationToken = default) => throw new NotSupportedException();

        public Task RecordFaultAsync(string envelopeId, string reason, string errorMessage, string errorDetails, CancellationToken cancellationToken = default) => throw new NotSupportedException();

        public Task RecordCancellationAsync(string envelopeId) => throw new NotSupportedException();

        private static CommandEnvelope CreateEnvelope(CommandEnvelopeRequest request)
        {
            var hash = SHA256.HashData(Encoding.UTF8.GetBytes($"{request.CommandId}|{request.PayloadJson}"));
            return new CommandEnvelope
            {
                Id = Id<CommandEnvelope>.New(),
                CorrelationId = Id<CorrelationScope>.FromBytes(hash[..16]),
                CommandTypeFullName = request.CommandId,
                PayloadJson = request.PayloadJson,
                Status = ActionExecutionStatus.Pending,
                NextAttemptAt = DateTimeOffset.UtcNow
            };
        }

        public Task<List<CommandEnvelope>> GetPendingUndispatchedAsync(CancellationToken cancellationToken = default)
        {
            return Task.FromResult(Envelopes
                .Where(e => e.Status == ActionExecutionStatus.Pending && e.DispatchedAt == null)
                .ToList());
        }

        public Task<List<CommandEnvelope>> GetFailedAsync(CancellationToken cancellationToken = default)
        {
            return Task.FromResult(Envelopes
                .Where(e => e.Status == ActionExecutionStatus.Failed)
                .ToList());
        }

        public Task<List<CommandEnvelope>> GetDeadLetteredAsync(CancellationToken cancellationToken = default)
        {
            return Task.FromResult(Envelopes
                .Where(e => e.Status == ActionExecutionStatus.DeadLettered)
                .ToList());
        }

        public Task<int> CountByStatusAsync(ActionExecutionStatus status, CancellationToken cancellationToken = default)
        {
            return Task.FromResult(Envelopes.Count(e => e.Status == status));
        }

        public Task<int> DeleteCompletedOlderThanAsync(DateTimeOffset cutoffDate, CancellationToken cancellationToken = default)
        {
            var toDelete = Envelopes
                .Where(e => e.Status == ActionExecutionStatus.Completed && e.CompletedAt.HasValue && e.CompletedAt < cutoffDate)
                .ToList();

            var count = toDelete.Count;
            foreach (var e in toDelete)
            {
                Envelopes.Remove(e);
            }

            return Task.FromResult(count);
        }
    }

    private sealed class FakeUnitOfWork : IUnitOfWork
    {
        public int SaveCallCount { get; private set; }

        public Task<int> SaveChangesAsync(CancellationToken cancellationToken = default)
        {
            SaveCallCount++;
            return Task.FromResult(1);
        }

        public Task<IUnitOfWorkTransaction> BeginTransactionAsync(CancellationToken cancellationToken = default)
        {
            return Task.FromResult<IUnitOfWorkTransaction>(new FakeTransaction());
        }

        public void DetachEntity<TEntity>(TEntity entity) where TEntity : class { }

        public void Dispose() { }

        private sealed class FakeTransaction : IUnitOfWorkTransaction
        {
            public Task CommitAsync(CancellationToken cancellationToken = default) => Task.CompletedTask;
            public Task RollbackAsync(CancellationToken cancellationToken = default) => Task.CompletedTask;
            public ValueTask DisposeAsync() => ValueTask.CompletedTask;
        }
    }


    private sealed class FakeLogger<T> : ILogger<T>
    {
        public List<string> Messages { get; } = new();

        public IDisposable? BeginScope<TState>(TState state) where TState : notnull => null;
        public bool IsEnabled(LogLevel logLevel) => true;
        public void Log<TState>(LogLevel logLevel, EventId eventId, TState state, Exception? exception, Func<TState, Exception?, string> formatter) =>
            Messages.Add(formatter(state, exception));
    }

    // Test command types
    private class TestCommand : IActionCommand
    {
        public string Value { get; set; } = string.Empty;
    }

    private class DerivedTestCommand : TestCommand
    {
        public string DerivedValue { get; set; } = string.Empty;
    }

    private sealed class UnregisteredCommand : IActionCommand;

    private sealed class CanonicalNameCollisionCommand : IActionCommand;

    // Test action handlers
    private sealed class TestActionHandler1 : IBackgroundAction<TestCommand>
    {
        public Task ExecuteAsync(TestCommand command, CancellationToken cancellationToken = default)
        {
            return Task.CompletedTask;
        }
    }

    private sealed class TestActionHandler2 : IBackgroundAction<TestCommand>
    {
        public Task ExecuteAsync(TestCommand command, CancellationToken cancellationToken = default)
        {
            return Task.CompletedTask;
        }
    }

    private sealed class TestActionHandlerSuccess : IBackgroundAction<TestCommand>
    {
        public Task ExecuteAsync(TestCommand command, CancellationToken cancellationToken = default)
        {
            return Task.CompletedTask;
        }
    }

    private sealed class UnregisteredCommandHandler : IBackgroundAction<UnregisteredCommand>
    {
        public Task ExecuteAsync(UnregisteredCommand command, CancellationToken cancellationToken = default) =>
            Task.CompletedTask;
    }

    private sealed class CanonicalNameCollisionHandler : IBackgroundAction<CanonicalNameCollisionCommand>
    {
        public Task ExecuteAsync(
            CanonicalNameCollisionCommand command,
            CancellationToken cancellationToken = default) =>
            Task.CompletedTask;
    }
}
