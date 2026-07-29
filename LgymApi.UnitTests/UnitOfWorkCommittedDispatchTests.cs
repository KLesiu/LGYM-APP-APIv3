using FluentAssertions;
using LgymApi.Application.Repositories;
using LgymApi.Domain.Entities;
using LgymApi.Domain.Enums;
using LgymApi.Domain.ValueObjects;
using LgymApi.Infrastructure.Data;
using LgymApi.Infrastructure.UnitOfWork;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Storage;
using Microsoft.Extensions.Logging.Abstractions;
using NSubstitute;

namespace LgymApi.UnitTests;

[TestFixture]
public sealed class UnitOfWorkCommittedDispatchTests
{
    [Test]
    public async Task SaveChangesAsync_DispatchesCommittedIntents_AfterPersistenceSucceeds()
    {
        var databaseName = $"uow-dispatch-{Id<UnitOfWorkCommittedDispatchTests>.New()}";
        var options = new DbContextOptionsBuilder<AppDbContext>()
            .UseInMemoryDatabase(databaseName)
            .Options;

        await using var dbContext = new AppDbContext(options);
        var envelopeId = Id<CommandEnvelope>.New();
        var persistedBeforeDispatch = false;
        var dispatcher = new RecordingCommittedIntentDispatcher(async () =>
        {
            await using var verificationContext = new AppDbContext(options);
            persistedBeforeDispatch = await verificationContext.CommandEnvelopes
                .AnyAsync(envelope => envelope.Id == envelopeId);
        });
        var unitOfWork = new EfUnitOfWork(dbContext, dispatcher, NullLogger<EfUnitOfWork>.Instance);

        dbContext.CommandEnvelopes.Add(new CommandEnvelope
        {
            Id = envelopeId,
            CorrelationId = Id<CorrelationScope>.New(),
            CommandTypeFullName = "Test.Command",
            PayloadJson = "{}",
            Status = ActionExecutionStatus.Pending
        });

        await unitOfWork.SaveChangesAsync();

        dispatcher.CallCount.Should().Be(1);
        persistedBeforeDispatch.Should().BeTrue();
    }

    [Test]
    public async Task CommitAsync_WhenDispatchFails_DoesNotThrowAndKeepsRecoveryPath()
    {
        var dbTransaction = new FakeDbContextTransaction();
        var dispatcher = new ThrowingCommittedIntentDispatcher();
        var options = new DbContextOptionsBuilder<AppDbContext>()
            .UseInMemoryDatabase($"uow-commit-dispatch-{Id<UnitOfWorkCommittedDispatchTests>.New()}")
            .Options;
        await using var dbContext = new AppDbContext(options);
        var transaction = new EfUnitOfWorkTransaction(dbTransaction, dbContext, dispatcher, NullLogger<EfUnitOfWork>.Instance);

        var action = async () => await transaction.CommitAsync();
        await action.Should().NotThrowAsync();

        dbTransaction.CommitCalls.Should().Be(1);
        dispatcher.CallCount.Should().Be(1);
    }

    [Test]
    public async Task BeginTransactionAsync_WhenUsingInMemoryProvider_ReturnsNoOpTransaction()
    {
        var options = new DbContextOptionsBuilder<AppDbContext>()
            .UseInMemoryDatabase($"uow-noop-transaction-{Id<UnitOfWorkCommittedDispatchTests>.New()}")
            .Options;
        await using var dbContext = new AppDbContext(options);
        var unitOfWork = new EfUnitOfWork(dbContext, Substitute.For<ICommittedIntentDispatcher>(), NullLogger<EfUnitOfWork>.Instance);

        var transaction = await unitOfWork.BeginTransactionAsync();

        transaction.GetType().Name.Should().Be("NoOpUnitOfWorkTransaction");
    }

    [Test]
    public async Task NoOpTransaction_RollbackClearsUncommittedChangesButCannotUndoSavedChanges()
    {
        var options = new DbContextOptionsBuilder<AppDbContext>()
            .UseInMemoryDatabase($"uow-noop-rollback-{Id<UnitOfWorkCommittedDispatchTests>.New()}")
            .Options;
        var savedEnvelopeId = Id<CommandEnvelope>.New();

        await using (var dbContext = new AppDbContext(options))
        {
            var unitOfWork = new EfUnitOfWork(dbContext);
            await using (var uncommittedTransaction = await unitOfWork.BeginTransactionAsync())
            {
                dbContext.CommandEnvelopes.Add(CreateEnvelope(Id<CommandEnvelope>.New(), "Uncommitted.Command"));
                await uncommittedTransaction.RollbackAsync();
            }

            dbContext.ChangeTracker.Entries().Should().BeEmpty();

            await using var savedTransaction = await unitOfWork.BeginTransactionAsync();
            dbContext.CommandEnvelopes.Add(CreateEnvelope(savedEnvelopeId, "Saved.Command"));
            await unitOfWork.SaveChangesAsync();
            await savedTransaction.RollbackAsync();
        }

        await using var verificationContext = new AppDbContext(options);
        verificationContext.CommandEnvelopes.Should().ContainSingle(envelope => envelope.Id == savedEnvelopeId);
    }

    [Test]
    public void DetachEntity_WhenEntityIsTracked_SetsDetachedState()
    {
        var options = new DbContextOptionsBuilder<AppDbContext>()
            .UseInMemoryDatabase($"uow-detach-{Id<UnitOfWorkCommittedDispatchTests>.New()}")
            .Options;
        using var dbContext = new AppDbContext(options);
        var envelope = new CommandEnvelope
        {
            Id = Id<CommandEnvelope>.New(),
            CorrelationId = Id<CorrelationScope>.New(),
            CommandTypeFullName = "Detach.Command",
            PayloadJson = "{}",
            Status = ActionExecutionStatus.Pending
        };
        dbContext.CommandEnvelopes.Add(envelope);
        var unitOfWork = new EfUnitOfWork(dbContext, null, NullLogger<EfUnitOfWork>.Instance);

        unitOfWork.DetachEntity(envelope);

        dbContext.Entry(envelope).State.Should().Be(EntityState.Detached);
    }

    [Test]
    public async Task BeginTransactionAsync_WhenUsingRelationalProvider_ReturnsEfTransaction()
    {
        await using var connection = new SqliteConnection("Data Source=:memory:");
        await connection.OpenAsync();
        var options = new DbContextOptionsBuilder<AppDbContext>()
            .UseSqlite(connection)
            .Options;
        await using var dbContext = new AppDbContext(options);
        var unitOfWork = new EfUnitOfWork(dbContext, null, NullLogger<EfUnitOfWork>.Instance);

        var transaction = await unitOfWork.BeginTransactionAsync();

        transaction.Should().BeOfType<EfUnitOfWorkTransaction>();
        await transaction.DisposeAsync();
    }

    [Test]
    public async Task SaveChangesAsync_WhenDispatcherThrows_DoesNotBubbleException()
    {
        var options = new DbContextOptionsBuilder<AppDbContext>()
            .UseInMemoryDatabase($"uow-dispatch-throw-{Id<UnitOfWorkCommittedDispatchTests>.New()}")
            .Options;
        await using var dbContext = new AppDbContext(options);
        var dispatcher = new ThrowingCommittedIntentDispatcher();
        var unitOfWork = new EfUnitOfWork(dbContext, dispatcher, NullLogger<EfUnitOfWork>.Instance);

        var action = async () => await unitOfWork.SaveChangesAsync();

        await action.Should().NotThrowAsync();
        dispatcher.CallCount.Should().Be(1);
    }

    private static CommandEnvelope CreateEnvelope(Id<CommandEnvelope> id, string commandType) => new()
    {
        Id = id,
        CorrelationId = Id<CorrelationScope>.New(),
        CommandTypeFullName = commandType,
        PayloadJson = "{}",
        Status = ActionExecutionStatus.Pending
    };

    private sealed class RecordingCommittedIntentDispatcher(Func<Task>? onDispatch = null) : ICommittedIntentDispatcher
    {
        public int CallCount { get; private set; }

        public async Task DispatchCommittedIntentsAsync(CancellationToken cancellationToken = default)
        {
            CallCount += 1;
            if (onDispatch is not null)
            {
                await onDispatch();
            }
        }
    }

    private sealed class ThrowingCommittedIntentDispatcher : ICommittedIntentDispatcher
    {
        public int CallCount { get; private set; }

        public Task DispatchCommittedIntentsAsync(CancellationToken cancellationToken = default)
        {
            CallCount += 1;
            throw new InvalidOperationException("Simulated scheduler outage after commit");
        }
    }

    private sealed class FakeDbContextTransaction : IDbContextTransaction
    {
        public Guid TransactionId { get; } = Guid.NewGuid();
        public int CommitCalls { get; private set; }
        public bool SupportsSavepoints => false;

        public void Commit()
        {
            CommitCalls += 1;
        }

        public Task CommitAsync(CancellationToken cancellationToken = default)
        {
            CommitCalls += 1;
            return Task.CompletedTask;
        }

        public void Rollback()
        {
        }

        public Task RollbackAsync(CancellationToken cancellationToken = default)
        {
            return Task.CompletedTask;
        }

        public void CreateSavepoint(string name)
        {
            throw new NotSupportedException();
        }

        public Task CreateSavepointAsync(string name, CancellationToken cancellationToken = default)
        {
            throw new NotSupportedException();
        }

        public void RollbackToSavepoint(string name)
        {
            throw new NotSupportedException();
        }

        public Task RollbackToSavepointAsync(string name, CancellationToken cancellationToken = default)
        {
            throw new NotSupportedException();
        }

        public void ReleaseSavepoint(string name)
        {
            throw new NotSupportedException();
        }

        public Task ReleaseSavepointAsync(string name, CancellationToken cancellationToken = default)
        {
            throw new NotSupportedException();
        }

        public void Dispose()
        {
        }

        public ValueTask DisposeAsync()
        {
            return ValueTask.CompletedTask;
        }
    }
}
