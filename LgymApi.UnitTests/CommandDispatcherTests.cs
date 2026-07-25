using LgymApi.Application.Repositories;
using LgymApi.Application.Coaching.Contracts.BackgroundCommands;
using LgymApi.Application.Identity.Contracts.BackgroundCommands;
using LgymApi.Application.Nutrition.Contracts.BackgroundCommands;
using LgymApi.Application.Platform.Contracts.BackgroundCommands;
using LgymApi.Application.Reporting.Contracts.BackgroundCommands;
using LgymApi.Application.WorkoutProgress.Contracts.BackgroundCommands;
using LgymApi.BackgroundWorker;
using LgymApi.BackgroundWorker.Actions.Contracts;
using LgymApi.BackgroundWorker.Runtime;
using LgymApi.Domain.Entities;
using LgymApi.Domain.Enums;
using LgymApi.Domain.ValueObjects;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Npgsql;
using NUnit.Framework;
using FluentAssertions;

namespace LgymApi.UnitTests;

/// <summary>
/// Tests for CommandDispatcher deterministic correlation ID generation.
/// </summary>
[TestFixture]
public sealed class CommandDispatcherTests
{
    private FakeCommandEnvelopeRepository _repository = null!;
    private FakeUnitOfWork _unitOfWork = null!;
    private Microsoft.Extensions.DependencyInjection.ServiceProvider _serviceProvider = null!;
    private CommandContractRegistry _commandContractRegistry = null!;
    private FakeLogger _logger = null!;

    [SetUp]
    public void SetUp()
    {
        _repository = new FakeCommandEnvelopeRepository();
        _unitOfWork = new FakeUnitOfWork();
        _logger = new FakeLogger();
        _commandContractRegistry = CommandContractRegistry.CreateDefault();
    }

    [TearDown]
    public void TearDown()
    {
        _serviceProvider?.Dispose();
    }

    [Test]
    public async Task ComputeDeterministicCorrelationId_IsDeterministic()
    {
        // Arrange
        var invitationId = Id<TrainerInvitation>.New();

        var services = new ServiceCollection();
        services.AddSingleton<ILogger<CommandDispatcher>>(_ => _logger);
        services.AddScoped<IBackgroundAction<InvitationAcceptedCommand>, FakeInvitationAcceptedHandler>();
        _serviceProvider = services.BuildServiceProvider();

        var dispatcher = new CommandDispatcher(
            CreateActionResolver(),
            _commandContractRegistry,
            _repository,
            _unitOfWork,
            _logger);

        // Act - Enqueue the same command 100 times
        var correlationIds = new List<Id<CorrelationScope>>();
        for (int i = 0; i < 100; i++)
        {
            var cmd = new InvitationAcceptedCommand { InvitationId = invitationId };
            await dispatcher.EnqueueAsync(cmd);

            // Get the correlation ID from the repository
            var envelopes = _repository.GetAllEnvelopes();
            var lastEnvelope = envelopes.LastOrDefault();
            if (lastEnvelope != null)
            {
                correlationIds.Add(lastEnvelope.CorrelationId);
            }
        }

        // Assert - All correlation IDs should be identical
        correlationIds.Should().HaveCount(100);
        correlationIds.Distinct().Should().HaveCount(1, "All 100 identical commands should produce the same correlation ID");
    }

    [Test]
    public async Task ComputeDeterministicCorrelationId_DifferentCommands_DifferentIds()
    {
        // Arrange
        var invitationId1 = Id<TrainerInvitation>.New();
        var invitationId2 = Id<TrainerInvitation>.New();

        var services = new ServiceCollection();
        services.AddSingleton<ILogger<CommandDispatcher>>(_ => _logger);
        services.AddScoped<IBackgroundAction<InvitationAcceptedCommand>, FakeInvitationAcceptedHandler>();
        _serviceProvider = services.BuildServiceProvider();

        var dispatcher = new CommandDispatcher(
            CreateActionResolver(),
            _commandContractRegistry,
            _repository,
            _unitOfWork,
            _logger);

        // Act - Enqueue commands with different InvitationIds
        var command1 = new InvitationAcceptedCommand { InvitationId = invitationId1 };
        var command2 = new InvitationAcceptedCommand { InvitationId = invitationId2 };

        await dispatcher.EnqueueAsync(command1);
        await dispatcher.EnqueueAsync(command2);

        // Assert - Should have two different correlation IDs
        var envelopes = _repository.GetAllEnvelopes();
        envelopes.Should().HaveCount(2);
        envelopes[0].CorrelationId.Should().NotBe(envelopes[1].CorrelationId, "Different InvitationIds should produce different correlation IDs");
    }

    [Test]
    public async Task EnqueueAsync_SameInvitationIdDifferentInstances_SameCorrelationId()
    {
        // Arrange
        var invitationId = Id<TrainerInvitation>.New();

        var services = new ServiceCollection();
        services.AddSingleton<ILogger<CommandDispatcher>>(_ => _logger);
        services.AddScoped<IBackgroundAction<InvitationAcceptedCommand>, FakeInvitationAcceptedHandler>();
        _serviceProvider = services.BuildServiceProvider();

        var dispatcher = new CommandDispatcher(
            CreateActionResolver(),
            _commandContractRegistry,
            _repository,
            _unitOfWork,
            _logger);

        // Act - Create two separate command instances with the same InvitationId
        var command1 = new InvitationAcceptedCommand { InvitationId = invitationId };
        var command2 = new InvitationAcceptedCommand { InvitationId = invitationId };

        await dispatcher.EnqueueAsync(command1);
        await dispatcher.EnqueueAsync(command2);

        // Assert - Should have only one envelope (idempotent)
        var envelopes = _repository.GetAllEnvelopes();
        envelopes.Should().HaveCount(1, "Same InvitationId should produce same correlation ID and be deduplicated");
    }

    [Test]
    public async Task EnqueueAsync_SaveChangesUniqueViolationOnEnvelopeConstraint_UsesDuplicateEnvelopePath()
    {
        // Arrange
        var invitationId = Id<TrainerInvitation>.New();

        var services = new ServiceCollection();
        services.AddSingleton<ILogger<CommandDispatcher>>(_ => _logger);
        services.AddScoped<IBackgroundAction<InvitationAcceptedCommand>, FakeInvitationAcceptedHandler>();
        _serviceProvider = services.BuildServiceProvider();

        _unitOfWork.SaveChangesException = CreateDuplicateEnvelopeDbUpdateException();

        var dispatcher = new CommandDispatcher(
            CreateActionResolver(),
            _commandContractRegistry,
            _repository,
            _unitOfWork,
            _logger);

        var command = new InvitationAcceptedCommand { InvitationId = invitationId };

        // Act
        var act = () => dispatcher.EnqueueAsync(command);

        // Assert
        await act.Should().NotThrowAsync();
        _unitOfWork.SaveCallCount.Should().Be(1);
        _repository.GetAllEnvelopes().Should().HaveCount(1);
    }

    [Test]
    public async Task EnqueueAsync_SaveChangesNonMatchingDbUpdateException_Bubbles()
    {
        // Arrange
        var invitationId = Id<TrainerInvitation>.New();

        var services = new ServiceCollection();
        services.AddSingleton<ILogger<CommandDispatcher>>(_ => _logger);
        services.AddScoped<IBackgroundAction<InvitationAcceptedCommand>, FakeInvitationAcceptedHandler>();
        _serviceProvider = services.BuildServiceProvider();

        _unitOfWork.SaveChangesException = new DbUpdateException("boom", new InvalidOperationException("not postgres"));

        var dispatcher = new CommandDispatcher(
            CreateActionResolver(),
            _commandContractRegistry,
            _repository,
            _unitOfWork,
            _logger);

        var command = new InvitationAcceptedCommand { InvitationId = invitationId };

        // Act
        var act = () => dispatcher.EnqueueAsync(command);

        // Assert
        await act.Should().ThrowAsync<DbUpdateException>().WithMessage("boom");
        _unitOfWork.SaveCallCount.Should().Be(1);
        _repository.GetAllEnvelopes().Should().HaveCount(1);
    }

    [Test]
    public async Task EnqueueAsync_SaveChangesDuplicateViolationButEnvelopeMissing_Throws()
    {
        // Arrange
        var invitationId = Id<TrainerInvitation>.New();

        var services = new ServiceCollection();
        services.AddSingleton<ILogger<CommandDispatcher>>(_ => _logger);
        services.AddScoped<IBackgroundAction<InvitationAcceptedCommand>, FakeInvitationAcceptedHandler>();
        _serviceProvider = services.BuildServiceProvider();

        _repository.ReturnNullOnFindByCorrelationId = true;
        _unitOfWork.SaveChangesException = CreateDuplicateEnvelopeDbUpdateException();

        var dispatcher = new CommandDispatcher(
            CreateActionResolver(),
            _commandContractRegistry,
            _repository,
            _unitOfWork,
            _logger);

        var command = new InvitationAcceptedCommand { InvitationId = invitationId };

        // Act
        var act = () => dispatcher.EnqueueAsync(command);

        // Assert - edge case: constraint violation but envelope not found must bubble up
        await act.Should().ThrowAsync<DbUpdateException>();
        _unitOfWork.SaveCallCount.Should().Be(1);
    }

    [TestCaseSource(typeof(LegacyCommandContractManifest), nameof(LegacyCommandContractManifest.CommandCases))]
    public async Task EnqueueAsync_PersistsLegacyGoldenVector(LegacyCommandContract contract)
    {
        // Given
        var services = new ServiceCollection();
        services.AddSingleton<ILogger<CommandDispatcher>>(_ => _logger);
        services.AddScoped(typeof(IBackgroundAction<>), typeof(LegacyNoOpBackgroundAction<>));
        _serviceProvider = services.BuildServiceProvider();
        var dispatcher = new CommandDispatcher(
            CreateActionResolver(),
            _commandContractRegistry,
            _repository,
            _unitOfWork,
            _logger);

        // When
        await EnqueueLegacyCommandAsync(dispatcher, contract.Command);
        var envelope = _repository.GetAllEnvelopes().Single();
        var actual = new PersistedCommandVector(
            envelope.CommandTypeFullName,
            envelope.PayloadJson,
            $"{envelope.CommandTypeFullName}|{envelope.PayloadJson}",
            envelope.CorrelationId.ToString());

        // Then
        actual.Should().Be(new PersistedCommandVector(
            contract.CanonicalId,
            contract.PayloadJson,
            contract.CorrelationInput,
            contract.CorrelationId));
    }

    private static Task EnqueueLegacyCommandAsync(CommandDispatcher dispatcher, IActionCommand command) => command switch
    {
        UserRegisteredCommand typedCommand => dispatcher.EnqueueAsync(typedCommand),
        TrainingCompletedCommand typedCommand => dispatcher.EnqueueAsync(typedCommand),
        InvitationCreatedCommand typedCommand => dispatcher.EnqueueAsync(typedCommand),
        InvitationAcceptedCommand typedCommand => dispatcher.EnqueueAsync(typedCommand),
        InvitationRevokedCommand typedCommand => dispatcher.EnqueueAsync(typedCommand),
        DietPlanUpdatedInAppNotificationCommand typedCommand => dispatcher.EnqueueAsync(typedCommand),
        TraineeNoteUpdatedInAppNotificationCommand typedCommand => dispatcher.EnqueueAsync(typedCommand),
        ReportSubmissionCreatedInAppNotificationCommand typedCommand => dispatcher.EnqueueAsync(typedCommand),
        ReportSubmissionAcceptedProgressCommand typedCommand => dispatcher.EnqueueAsync(typedCommand),
        ReportRequestCreatedInAppNotificationCommand typedCommand => dispatcher.EnqueueAsync(typedCommand),
        ReportFeedbackAddedInAppNotificationCommand typedCommand => dispatcher.EnqueueAsync(typedCommand),
        TrainerInvitationAcceptedInAppNotificationCommand typedCommand => dispatcher.EnqueueAsync(typedCommand),
        TrainerInvitationCreatedInAppNotificationCommand typedCommand => dispatcher.EnqueueAsync(typedCommand),
        TrainerInvitationRejectedInAppNotificationCommand typedCommand => dispatcher.EnqueueAsync(typedCommand),
        TrainerRelationshipEndedInAppNotificationCommand typedCommand => dispatcher.EnqueueAsync(typedCommand),
        _ => throw new ArgumentOutOfRangeException(nameof(command), command.GetType(), "Command is absent from the legacy manifest.")
    };

    private sealed record PersistedCommandVector(
        string CanonicalId,
        string PayloadJson,
        string CorrelationInput,
        string CorrelationId);

    private sealed class LegacyNoOpBackgroundAction<TCommand> : IBackgroundAction<TCommand>
        where TCommand : IActionCommand
    {
        public Task ExecuteAsync(TCommand command, CancellationToken cancellationToken = default) => Task.CompletedTask;
    }

    // Test handler
    private sealed class FakeInvitationAcceptedHandler : IBackgroundAction<InvitationAcceptedCommand>
    {
        public Task ExecuteAsync(InvitationAcceptedCommand command, CancellationToken cancellationToken = default)
        {
            return Task.CompletedTask;
        }
    }

    // Fake implementations
    private sealed class FakeCommandEnvelopeRepository : ICommandEnvelopeRepository
    {
        private readonly Dictionary<Id<CommandEnvelope>, CommandEnvelope> _envelopes = new();
        public int UpdateCallCount { get; private set; }
        public bool ReturnNullOnFindByCorrelationId { get; set; }

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
            return Task.FromResult(ReturnNullOnFindByCorrelationId
                ? null
                : _envelopes.Values.FirstOrDefault(e => e.CorrelationId == correlationId));
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

        public List<CommandEnvelope> GetAllEnvelopes() => _envelopes.Values.ToList();
    }

    private sealed class FakeUnitOfWork : IUnitOfWork
    {
        public int SaveCallCount { get; private set; }
        public Exception? SaveChangesException { get; set; }

        public Task<int> SaveChangesAsync(CancellationToken cancellationToken = default)
        {
            SaveCallCount++;
            if (SaveChangesException != null)
            {
                throw SaveChangesException;
            }

            return Task.FromResult(1);
        }

        public Task<IUnitOfWorkTransaction> BeginTransactionAsync(CancellationToken cancellationToken = default)
            => throw new NotSupportedException();

        public void DetachEntity<TEntity>(TEntity entity) where TEntity : class { }
    }

    private IBackgroundActionResolver CreateActionResolver() =>
        new BackgroundActionResolver(_serviceProvider.GetRequiredService<IServiceScopeFactory>());

    private static DbUpdateException CreateDuplicateEnvelopeDbUpdateException()
    {
        var postgresException = new PostgresException(
            "duplicate key value violates unique constraint",
            "ERROR",
            "ERROR",
            PostgresErrorCodes.UniqueViolation,
            constraintName: "IX_CommandEnvelopes_CorrelationId");

        return new DbUpdateException("duplicate envelope", postgresException);
    }

    private sealed class FakeLogger : ILogger<CommandDispatcher>
    {
        public IDisposable? BeginScope<TState>(TState state) where TState : notnull => null;
        public bool IsEnabled(LogLevel logLevel) => false;
        public void Log<TState>(LogLevel logLevel, EventId eventId, TState state, Exception? exception, Func<TState, Exception?, string> formatter) { }
    }
}
