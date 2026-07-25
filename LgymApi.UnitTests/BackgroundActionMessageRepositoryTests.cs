using FluentAssertions;
using LgymApi.Domain.ValueObjects;
using LgymApi.Domain.Entities;
using LgymApi.Domain.Enums;
using LgymApi.Infrastructure.Data;
using LgymApi.Infrastructure.Repositories;
using Microsoft.EntityFrameworkCore;
using NUnit.Framework;

namespace LgymApi.UnitTests;

/// <summary>
/// Tests CommandEnvelopeRepository from Task 9 infrastructure.
/// Named to match BackgroundActionMessageRepository filter for QA discoverability.
/// </summary>
[TestFixture]
public sealed class BackgroundActionMessageRepositoryTests
{
    [Test]
    public async Task CommandEnvelope_AddAsync_AddsEnvelopeToContext()
    {
        // Arrange
        var options = new DbContextOptionsBuilder<AppDbContext>()
            .UseInMemoryDatabase($"command-envelope-repo-{Id<CommandEnvelope>.New():N}")
            .Options;

        await using var dbContext = new AppDbContext(options);
        var repository = new CommandEnvelopeRepository(dbContext);

        var envelope = new CommandEnvelope
        {
            CorrelationId = Id<CorrelationScope>.New(),
            CommandTypeFullName = "TestNamespace.TestCommand, TestAssembly",
            PayloadJson = "{\"test\":\"data\"}",
            Status = ActionExecutionStatus.Pending,
            CreatedAt = DateTimeOffset.UtcNow,
            UpdatedAt = DateTimeOffset.UtcNow
        };

        // Act
        await repository.AddAsync(envelope);
        await dbContext.SaveChangesAsync();

        // Assert
        var saved = await dbContext.CommandEnvelopes.FindAsync(envelope.Id);
        saved.Should().NotBeNull();
        saved.CommandTypeFullName.Should().Be("TestNamespace.TestCommand, TestAssembly");
        saved.Status.Should().Be(ActionExecutionStatus.Pending);
    }

    [Test]
    public async Task CommandEnvelope_FindByIdAsync_ReturnsEnvelopeWhenExists()
    {
        // Arrange
        var options = new DbContextOptionsBuilder<AppDbContext>()
            .UseInMemoryDatabase($"command-envelope-repo-{Id<CommandEnvelope>.New():N}")
            .Options;

        await using var dbContext = new AppDbContext(options);
        var repository = new CommandEnvelopeRepository(dbContext);

        var envelope = new CommandEnvelope
        {
            CorrelationId = Id<CorrelationScope>.New(),
            CommandTypeFullName = "TestNamespace.TestCommand, TestAssembly",
            PayloadJson = "{}",
            Status = ActionExecutionStatus.Pending,
            CreatedAt = DateTimeOffset.UtcNow,
            UpdatedAt = DateTimeOffset.UtcNow
        };

        await repository.AddAsync(envelope);
        await dbContext.SaveChangesAsync();

        // Act
        var found = await repository.FindByIdAsync(envelope.Id);

        // Assert
        found.Should().NotBeNull();
        found.Id.Should().Be(envelope.Id);
    }

    [Test]
    public async Task CommandEnvelope_FindByIdAsync_ReturnsNullWhenNotFound()
    {
        // Arrange
        var options = new DbContextOptionsBuilder<AppDbContext>()
            .UseInMemoryDatabase($"command-envelope-repo-{Id<CommandEnvelope>.New():N}")
            .Options;

        await using var dbContext = new AppDbContext(options);
        var repository = new CommandEnvelopeRepository(dbContext);

        // Act
        var found = await repository.FindByIdAsync(Id<CommandEnvelope>.New());

        // Assert
        found.Should().BeNull();
    }

    [Test]
    public async Task CommandEnvelope_FindByCorrelationIdAsync_ReturnsEnvelopeWhenExists()
    {
        // Arrange
        var options = new DbContextOptionsBuilder<AppDbContext>()
            .UseInMemoryDatabase($"command-envelope-repo-{Id<CommandEnvelope>.New():N}")
            .Options;

        await using var dbContext = new AppDbContext(options);
        var repository = new CommandEnvelopeRepository(dbContext);

        var correlationId = Id<CorrelationScope>.New();
        var envelope = new CommandEnvelope
        {
            CorrelationId = correlationId,
            CommandTypeFullName = "TestNamespace.TestCommand, TestAssembly",
            PayloadJson = "{}",
            Status = ActionExecutionStatus.Pending,
            CreatedAt = DateTimeOffset.UtcNow,
            UpdatedAt = DateTimeOffset.UtcNow
        };

        await repository.AddAsync(envelope);
        await dbContext.SaveChangesAsync();

        // Act
        var found = await repository.FindByCorrelationIdAsync(correlationId);

        // Assert
        found.Should().NotBeNull();
        found.CorrelationId.Should().Be(correlationId);
    }

    [Test]
    public async Task CommandEnvelope_FindByCorrelationIdAsync_ReturnsNullWhenNotFound()
    {
        // Arrange
        var options = new DbContextOptionsBuilder<AppDbContext>()
            .UseInMemoryDatabase($"command-envelope-repo-{Id<CommandEnvelope>.New():N}")
            .Options;

        await using var dbContext = new AppDbContext(options);
        var repository = new CommandEnvelopeRepository(dbContext);

        // Act
        var found = await repository.FindByCorrelationIdAsync(Id<CorrelationScope>.New());

        // Assert
        found.Should().BeNull();
    }

    [Test]
    public async Task CommandEnvelope_GetPendingRetriesAsync_ReturnsFailedEnvelopesReadyForRetry()
    {
        // Arrange
        var options = new DbContextOptionsBuilder<AppDbContext>()
            .UseInMemoryDatabase($"command-envelope-repo-{Id<CommandEnvelope>.New():N}")
            .Options;

        await using var dbContext = new AppDbContext(options);
        var repository = new CommandEnvelopeRepository(dbContext);

        var readyForRetry = new CommandEnvelope
        {
            Id = Domain.ValueObjects.Id<CommandEnvelope>.New(),
            CorrelationId = Id<CorrelationScope>.New(),
            CommandTypeFullName = "TestNamespace.TestCommand, TestAssembly",
            PayloadJson = "{}",
            Status = ActionExecutionStatus.Failed,
            CreatedAt = DateTimeOffset.UtcNow.AddHours(-2),
            UpdatedAt = DateTimeOffset.UtcNow,
            NextAttemptAt = DateTimeOffset.UtcNow.AddMinutes(-5)
        };

        var notYetReady = new CommandEnvelope
        {
            Id = Domain.ValueObjects.Id<CommandEnvelope>.New(),
            CorrelationId = Id<CorrelationScope>.New(),
            CommandTypeFullName = "TestNamespace.TestCommand, TestAssembly",
            PayloadJson = "{}",
            Status = ActionExecutionStatus.Failed,
            CreatedAt = DateTimeOffset.UtcNow.AddHours(-1),
            UpdatedAt = DateTimeOffset.UtcNow,
            NextAttemptAt = DateTimeOffset.UtcNow.AddMinutes(30)
        };

        var succeeded = new CommandEnvelope
        {
            Id = Domain.ValueObjects.Id<CommandEnvelope>.New(),
            CorrelationId = Id<CorrelationScope>.New(),
            CommandTypeFullName = "TestNamespace.TestCommand, TestAssembly",
            PayloadJson = "{}",
            Status = ActionExecutionStatus.Completed,
            CreatedAt = DateTimeOffset.UtcNow.AddHours(-3),
            UpdatedAt = DateTimeOffset.UtcNow,
            CompletedAt = DateTimeOffset.UtcNow.AddHours(-2)
        };

        await repository.AddAsync(readyForRetry);
        await repository.AddAsync(notYetReady);
        await repository.AddAsync(succeeded);
        await dbContext.SaveChangesAsync();

        // Act
        var pending = await repository.GetPendingRetriesAsync();

        // Assert
        pending.Should().HaveCount(1);
        pending.First().Id.Should().Be(readyForRetry.Id);
    }

    [Test]
    public async Task CommandEnvelope_UpdateAsync_PersistsChanges()
    {
        // Arrange
        var options = new DbContextOptionsBuilder<AppDbContext>()
            .UseInMemoryDatabase($"command-envelope-repo-{Id<CommandEnvelope>.New():N}")
            .Options;

        await using var dbContext = new AppDbContext(options);
        var repository = new CommandEnvelopeRepository(dbContext);

        var envelope = new CommandEnvelope
        {
            CorrelationId = Id<CorrelationScope>.New(),
            CommandTypeFullName = "TestNamespace.TestCommand, TestAssembly",
            PayloadJson = "{}",
            Status = ActionExecutionStatus.Pending,
            CreatedAt = DateTimeOffset.UtcNow,
            UpdatedAt = DateTimeOffset.UtcNow
        };

        await repository.AddAsync(envelope);
        await dbContext.SaveChangesAsync();
        await dbContext.SaveChangesAsync();

        // Act
        envelope.Status = ActionExecutionStatus.Completed;
        envelope.CompletedAt = DateTimeOffset.UtcNow;
        envelope.UpdatedAt = DateTimeOffset.UtcNow;
        await repository.UpdateAsync(envelope);
        await dbContext.SaveChangesAsync();

        // Assert
        var updated = await dbContext.CommandEnvelopes.FindAsync(envelope.Id);
        updated.Should().NotBeNull();
        updated.Status.Should().Be(ActionExecutionStatus.Completed);
        updated.CompletedAt.Should().NotBeNull();
    }

}
