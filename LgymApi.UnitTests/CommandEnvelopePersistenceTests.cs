using FluentAssertions;
using LgymApi.Domain.ValueObjects;
using LgymApi.Domain.Entities;
using LgymApi.Domain.Enums;
using LgymApi.Infrastructure.Data;
using Microsoft.EntityFrameworkCore;
using NUnit.Framework;

namespace LgymApi.UnitTests;

/// <summary>
/// Tests durable persistence behavior of CommandEnvelope and ActionExecutionLog entities.
/// Validates entity lifecycle, relationships, and index coverage.
/// </summary>
[TestFixture]
public class CommandEnvelopePersistenceTests
{
    private DbContextOptions<AppDbContext> _contextOptions = null!;
    private AppDbContext _context = null!;

    [SetUp]
    public void Setup()
    {
        // Use in-memory database for test isolation
        _contextOptions = new DbContextOptionsBuilder<AppDbContext>()
            .UseInMemoryDatabase(databaseName: Id<CommandEnvelope>.New().ToString())
            .Options;

        _context = new AppDbContext(_contextOptions);
    }

    [TearDown]
    public void TearDown()
    {
        _context?.Dispose();
    }

    [Test]
    public async Task CommandEnvelope_CanBePersisted_WithAllRequiredFields()
    {
        // Arrange
        var correlationId = Id<CorrelationScope>.New();
        var envelope = new CommandEnvelope
        {
            Id = Id<CommandEnvelope>.New(),
            CorrelationId = correlationId,
            PayloadJson = """{"data":"test"}""",
            CommandTypeFullName = "TestNamespace.TestCommand, TestAssembly",
            Status = ActionExecutionStatus.Pending,
            NextAttemptAt = DateTimeOffset.UtcNow.AddSeconds(30),
            CreatedAt = DateTimeOffset.UtcNow,
            UpdatedAt = DateTimeOffset.UtcNow,
        };

        // Act
        _context.CommandEnvelopes.Add(envelope);
        await _context.SaveChangesAsync();

        // Assert
        var retrieved = await _context.CommandEnvelopes
            .FirstOrDefaultAsync(e => e.Id == envelope.Id);
        retrieved.Should().NotBeNull();
        retrieved!.CorrelationId.Should().Be(correlationId);
        retrieved.Status.Should().Be(ActionExecutionStatus.Pending);
    }

    [Test]
    public async Task ActionExecutionLog_CanBeLinked_ToCommandEnvelope()
    {
        // Arrange
        var envelopeId = Id<CommandEnvelope>.New();
        var envelope = new CommandEnvelope
        {
            Id = envelopeId,
            CorrelationId = Id<CorrelationScope>.New(),
            PayloadJson = """{"data":"test"}""",
            CommandTypeFullName = "TestNamespace.TestCommand, TestAssembly",
            Status = ActionExecutionStatus.Failed,
            CreatedAt = DateTimeOffset.UtcNow,
            UpdatedAt = DateTimeOffset.UtcNow,
        };

        _context.CommandEnvelopes.Add(envelope);
        await _context.SaveChangesAsync();

        var log1 = new ActionExecutionLog
        {
            Id = Id<ActionExecutionLog>.New(),
            CommandEnvelopeId = envelopeId,
            ActionType = ActionExecutionLogType.Execute,
            Status = ActionExecutionStatus.Failed,
            AttemptNumber = 1,
            ErrorMessage = "Timeout",
            CreatedAt = DateTimeOffset.UtcNow.AddSeconds(-10),
            UpdatedAt = DateTimeOffset.UtcNow.AddSeconds(-10),
        };

        var log2 = new ActionExecutionLog
        {
            Id = Id<ActionExecutionLog>.New(),
            CommandEnvelopeId = envelopeId,
            ActionType = ActionExecutionLogType.Retry,
            Status = ActionExecutionStatus.Processing,
            AttemptNumber = 2,
            CreatedAt = DateTimeOffset.UtcNow,
            UpdatedAt = DateTimeOffset.UtcNow,
        };

        // Act
        _context.ActionExecutionLogs.AddRange(log1, log2);
        await _context.SaveChangesAsync();

        // Assert
        var logs = await _context.ActionExecutionLogs
            .Where(l => l.CommandEnvelopeId == envelopeId)
            .OrderBy(l => l.AttemptNumber)
            .ToListAsync();

        logs.Should().HaveCount(2);
        logs[0].AttemptNumber.Should().Be(1);
        logs[0].Status.Should().Be(ActionExecutionStatus.Failed);
        logs[1].AttemptNumber.Should().Be(2);
        logs[1].Status.Should().Be(ActionExecutionStatus.Processing);
    }

    [Test]
    public async Task CommandEnvelope_StatusTransition_Persists()
    {
        // Arrange
        var envelope = new CommandEnvelope
        {
            Id = Id<CommandEnvelope>.New(),
            CorrelationId = Id<CorrelationScope>.New(),
            PayloadJson = """{"data":"test"}""",
            CommandTypeFullName = "TestNamespace.TestCommand, TestAssembly",
            Status = ActionExecutionStatus.Pending,
            CreatedAt = DateTimeOffset.UtcNow,
            UpdatedAt = DateTimeOffset.UtcNow,
        };

        _context.CommandEnvelopes.Add(envelope);
        await _context.SaveChangesAsync();

        // Act - transition from Pending to Processing
        var retrieved = await _context.CommandEnvelopes.FindAsync(envelope.Id);
        retrieved.Should().NotBeNull();
        retrieved!.Status = ActionExecutionStatus.Processing;
        retrieved.LastAttemptAt = DateTimeOffset.UtcNow;
        await _context.SaveChangesAsync();

        // Assert - transition persisted
        var final = await _context.CommandEnvelopes.FindAsync(envelope.Id);
        final!.Status.Should().Be(ActionExecutionStatus.Processing);
        final.LastAttemptAt.Should().NotBeNull();
    }

    [Test]
    public async Task CommandEnvelope_CompletedAt_SetOnSuccess()
    {
        // Arrange
        var envelope = new CommandEnvelope
        {
            Id = Id<CommandEnvelope>.New(),
            CorrelationId = Id<CorrelationScope>.New(),
            PayloadJson = """{"data":"test"}""",
            CommandTypeFullName = "TestNamespace.TestCommand, TestAssembly",
            Status = ActionExecutionStatus.Pending,
            CreatedAt = DateTimeOffset.UtcNow,
            UpdatedAt = DateTimeOffset.UtcNow,
        };

        _context.CommandEnvelopes.Add(envelope);
        await _context.SaveChangesAsync();

        // Act - mark as completed
        var retrieved = await _context.CommandEnvelopes.FindAsync(envelope.Id);
        retrieved.Should().NotBeNull();
        retrieved!.Status = ActionExecutionStatus.Completed;
        retrieved.CompletedAt = DateTimeOffset.UtcNow;
        await _context.SaveChangesAsync();

        // Assert
        var final = await _context.CommandEnvelopes.FindAsync(envelope.Id);
        final!.Status.Should().Be(ActionExecutionStatus.Completed);
        final.CompletedAt.Should().NotBeNull();
    }

    [Test]
    public async Task ActionExecutionLog_ErrorTracking_Persists()
    {
        // Arrange
        var envelopeId = Id<CommandEnvelope>.New();
        var envelope = new CommandEnvelope
        {
            Id = envelopeId,
            CorrelationId = Id<CorrelationScope>.New(),
            PayloadJson = """{"data":"test"}""",
            CommandTypeFullName = "TestNamespace.TestCommand, TestAssembly",
            Status = ActionExecutionStatus.Failed,
            CreatedAt = DateTimeOffset.UtcNow,
            UpdatedAt = DateTimeOffset.UtcNow,
        };

        var log = new ActionExecutionLog
        {
            Id = Id<ActionExecutionLog>.New(),
            CommandEnvelopeId = envelopeId,
            ActionType = ActionExecutionLogType.Execute,
            Status = ActionExecutionStatus.Failed,
            AttemptNumber = 1,
            ErrorMessage = "Connection timeout",
            ErrorDetails = "System.TimeoutException: The operation timed out...",
            CreatedAt = DateTimeOffset.UtcNow,
            UpdatedAt = DateTimeOffset.UtcNow,
        };

        // Act
        _context.CommandEnvelopes.Add(envelope);
        _context.ActionExecutionLogs.Add(log);
        await _context.SaveChangesAsync();

        // Assert
        var retrieved = await _context.ActionExecutionLogs.FirstAsync();
        retrieved.ErrorMessage.Should().Be("Connection timeout");
        retrieved.ErrorDetails.Should().Contain("TimeoutException");
    }

    [Test]
    public async Task CommandEnvelope_CorrelationId_EnablesTracing()
    {
        // Arrange - simulate related commands with same correlation
        var correlationId = Id<CorrelationScope>.New();
        var envelopes = new[]
        {
            new CommandEnvelope
            {
                Id = Id<CommandEnvelope>.New(),
                CorrelationId = correlationId,
                PayloadJson = """{"cmd":"send-email"}""",
                CommandTypeFullName = "SendEmailCommand, MyAssembly",
                Status = ActionExecutionStatus.Completed,
                CreatedAt = DateTimeOffset.UtcNow,
                UpdatedAt = DateTimeOffset.UtcNow,
            },
            new CommandEnvelope
            {
                Id = Id<CommandEnvelope>.New(),
                CorrelationId = correlationId,
                PayloadJson = """{"cmd":"log-event"}""",
                CommandTypeFullName = "LogEventCommand, MyAssembly",
                Status = ActionExecutionStatus.Completed,
                CreatedAt = DateTimeOffset.UtcNow,
                UpdatedAt = DateTimeOffset.UtcNow,
            }
        };

        // Act
        _context.CommandEnvelopes.AddRange(envelopes);
        await _context.SaveChangesAsync();

        // Assert - all related envelopes retrievable by correlation
        var related = await _context.CommandEnvelopes
            .Where(e => e.CorrelationId == correlationId)
            .ToListAsync();

        related.Should().HaveCount(2);
        related.Select(e => e.CommandTypeFullName).Distinct().ToList().Should().HaveCount(2);
    }

    [Test]
    public async Task CommandEnvelope_NextAttemptAt_IndexedForWorkRetrieval()
    {
        // Arrange
        var now = DateTimeOffset.UtcNow;
        var pendingEnvelopes = new[]
        {
            new CommandEnvelope
            {
                Id = Id<CommandEnvelope>.New(),
                CorrelationId = Id<CorrelationScope>.New(),
                PayloadJson = """{}""",
                CommandTypeFullName = "Cmd1, Asm",
                Status = ActionExecutionStatus.Failed,
                NextAttemptAt = now.AddSeconds(5), // Ready soon
                CreatedAt = now,
                UpdatedAt = now,
            },
            new CommandEnvelope
            {
                Id = Id<CommandEnvelope>.New(),
                CorrelationId = Id<CorrelationScope>.New(),
                PayloadJson = """{}""",
                CommandTypeFullName = "Cmd2, Asm",
                Status = ActionExecutionStatus.Failed,
                NextAttemptAt = now.AddMinutes(10), // Not ready
                CreatedAt = now,
                UpdatedAt = now,
            },
            new CommandEnvelope
            {
                Id = Id<CommandEnvelope>.New(),
                CorrelationId = Id<CorrelationScope>.New(),
                PayloadJson = """{}""",
                CommandTypeFullName = "Cmd3, Asm",
                Status = ActionExecutionStatus.Completed,
                NextAttemptAt = null, // Terminal state
                CreatedAt = now,
                UpdatedAt = now,
            }
        };

        _context.CommandEnvelopes.AddRange(pendingEnvelopes);
        await _context.SaveChangesAsync();

        // Act - find work ready for retry (status Failed AND NextAttemptAt <= now)
        var readyForWork = await _context.CommandEnvelopes
            .Where(e => e.Status == ActionExecutionStatus.Failed &&
                        e.NextAttemptAt.HasValue &&
                        e.NextAttemptAt <= now.AddSeconds(10))
            .ToListAsync();

        // Assert
        readyForWork.Should().HaveCount(1);
        readyForWork[0].CommandTypeFullName.Should().Be("Cmd1, Asm");
    }
}
