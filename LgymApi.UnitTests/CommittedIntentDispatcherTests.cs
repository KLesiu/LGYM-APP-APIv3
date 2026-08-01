using FluentAssertions;
using LgymApi.Application.Options;
using LgymApi.BackgroundWorker.Common;
using LgymApi.Domain.Entities;
using LgymApi.Domain.Enums;
using LgymApi.Domain.Notifications;
using LgymApi.Domain.ValueObjects;
using LgymApi.Infrastructure.Data;
using LgymApi.Infrastructure.Services;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using NUnit.Framework;

namespace LgymApi.UnitTests;

[TestFixture]
public sealed class CommittedIntentDispatcherTests
{
    private string _databaseName = null!;
    private DbContextOptions<AppDbContext> _options = null!;
    private ServiceProvider _provider = null!;
    private FakeActionScheduler _actionScheduler = null!;
    private FakeEmailScheduler _emailScheduler = null!;

    [SetUp]
    public void SetUp()
    {
        _databaseName = $"dispatcher-{Id<CommittedIntentDispatcherTests>.New()}";
        _actionScheduler = new FakeActionScheduler();
        _emailScheduler = new FakeEmailScheduler();

        _options = new DbContextOptionsBuilder<AppDbContext>()
            .UseInMemoryDatabase(_databaseName)
            .Options;

        var services = new ServiceCollection();
        services.AddLogging();
        services.AddScoped(_ => new AppDbContext(_options));
        services.AddSingleton<IActionMessageScheduler>(_ => _actionScheduler);
        services.AddSingleton<IEmailBackgroundScheduler>(_ => _emailScheduler);

        _provider = services.BuildServiceProvider();
    }

    [TearDown]
    public void TearDown()
    {
        _provider.Dispose();
    }

    [Test]
    public async Task DispatchCommittedIntentsAsync_WithPendingCommandEnvelopes_EnqueuesAndMarksDispatched()
    {
        var envelope = new CommandEnvelope
        {
            Id = Id<CommandEnvelope>.New(),
            Status = ActionExecutionStatus.Pending,
            PayloadJson = "{}",
            CommandTypeFullName = "LgymApi.BackgroundWorker.Actions.TestCommand"
        };

        await using (var seed = NewContext())
        {
            seed.CommandEnvelopes.Add(envelope);
            await seed.SaveChangesAsync();
        }

        var dispatcher = CreateDispatcher();
        await dispatcher.DispatchCommittedIntentsAsync();

        _actionScheduler.Enqueued.Should().ContainSingle().Which.Should().Be(envelope.Id);

        await using (var read = NewContext())
        {
            var loaded = await read.CommandEnvelopes.AsNoTracking().SingleAsync(e => e.Id == envelope.Id);
            loaded.DispatchedAt.Should().NotBeNull();
            loaded.SchedulerJobId.Should().Be("job-id");
        }
    }

    [Test]
    public async Task DispatchCommittedIntentsAsync_WithNoPendingWork_DoesNotEnqueue()
    {
        var dispatcher = CreateDispatcher();
        await dispatcher.DispatchCommittedIntentsAsync();

        _actionScheduler.Enqueued.Should().BeEmpty();
        _emailScheduler.Enqueued.Should().BeEmpty();
    }

    [Test]
    public async Task DispatchCommittedIntentsAsync_WithPendingNotifications_EnqueuesAndMarksDispatched()
    {
        var notification = new NotificationMessage
        {
            Id = Id<NotificationMessage>.New(),
            Status = EmailNotificationStatus.Pending,
            Type = EmailNotificationTypes.TrainerInvitation,
            Recipient = "trainee@example.com",
            PayloadJson = "{}",
            CorrelationId = Id<CorrelationScope>.New()
        };

        await using (var seed = NewContext())
        {
            seed.NotificationMessages.Add(notification);
            await seed.SaveChangesAsync();
        }

        var dispatcher = CreateDispatcher();
        await dispatcher.DispatchCommittedIntentsAsync();

        _emailScheduler.Enqueued.Should().ContainSingle().Which.Should().Be(notification.Id);

        await using (var read = NewContext())
        {
            var loaded = await read.NotificationMessages.AsNoTracking().SingleAsync(n => n.Id == notification.Id);
            loaded.DispatchedAt.Should().NotBeNull();
            loaded.SchedulerJobId.Should().Be("job-id");
        }
    }

    [Test]
    public async Task DispatchCommittedIntentsAsync_WithStaleSendingNotifications_Redispatches()
    {
        var notification = new NotificationMessage
        {
            Id = Id<NotificationMessage>.New(),
            Status = EmailNotificationStatus.Sending,
            Type = EmailNotificationTypes.TrainerInvitation,
            Recipient = "trainee@example.com",
            PayloadJson = "{}",
            CorrelationId = Id<CorrelationScope>.New(),
            DeliveredAt = null,
            LastAttemptAt = DateTimeOffset.UtcNow.AddMinutes(-10)
        };

        await using (var seed = NewContext())
        {
            seed.NotificationMessages.Add(notification);
            await seed.SaveChangesAsync();
        }

        var dispatcher = CreateDispatcher(new BackgroundCommandOptions { EmailSendLeaseSeconds = 30 });
        await dispatcher.DispatchCommittedIntentsAsync();

        _emailScheduler.Enqueued.Should().ContainSingle().Which.Should().Be(notification.Id);

        await using (var read = NewContext())
        {
            var loaded = await read.NotificationMessages.AsNoTracking().SingleAsync(n => n.Id == notification.Id);
            loaded.DispatchedAt.Should().NotBeNull();
        }
    }

    [Test]
    public async Task DispatchCommittedIntentsAsync_WhenActionSchedulerThrows_LeavesEnvelopeUndispatched()
    {
        _actionScheduler.ThrowOnEnqueue = true;
        var envelope = new CommandEnvelope
        {
            Id = Id<CommandEnvelope>.New(),
            Status = ActionExecutionStatus.Pending,
            PayloadJson = "{}",
            CommandTypeFullName = "LgymApi.BackgroundWorker.Actions.TestCommand"
        };

        await using (var seed = NewContext())
        {
            seed.CommandEnvelopes.Add(envelope);
            await seed.SaveChangesAsync();
        }

        var dispatcher = CreateDispatcher();
        await dispatcher.DispatchCommittedIntentsAsync();

        _actionScheduler.Enqueued.Should().BeEmpty();

        await using (var read = NewContext())
        {
            var loaded = await read.CommandEnvelopes.AsNoTracking().SingleAsync(e => e.Id == envelope.Id);
            loaded.DispatchedAt.Should().BeNull();
        }
    }

    [Test]
    public async Task DispatchCommittedIntentsAsync_WhenEmailSchedulerThrows_LeavesNotificationUndispatched()
    {
        _emailScheduler.ThrowOnEnqueue = true;
        var notification = new NotificationMessage
        {
            Id = Id<NotificationMessage>.New(),
            Status = EmailNotificationStatus.Pending,
            Type = EmailNotificationTypes.TrainerInvitation,
            Recipient = "trainee@example.com",
            PayloadJson = "{}",
            CorrelationId = Id<CorrelationScope>.New()
        };

        await using (var seed = NewContext())
        {
            seed.NotificationMessages.Add(notification);
            await seed.SaveChangesAsync();
        }

        var dispatcher = CreateDispatcher();
        await dispatcher.DispatchCommittedIntentsAsync();

        _emailScheduler.Enqueued.Should().BeEmpty();

        await using (var read = NewContext())
        {
            var loaded = await read.NotificationMessages.AsNoTracking().SingleAsync(n => n.Id == notification.Id);
            loaded.DispatchedAt.Should().BeNull();
        }
    }

    private AppDbContext NewContext() => new(_options);

    private CommittedIntentDispatcher CreateDispatcher(BackgroundCommandOptions? options = null)
    {
        var scopeFactory = _provider.GetRequiredService<IServiceScopeFactory>();
        var logger = _provider.GetRequiredService<ILogger<CommittedIntentDispatcher>>();
        return new CommittedIntentDispatcher(scopeFactory, logger, options ?? new BackgroundCommandOptions());
    }

    private sealed class FakeActionScheduler : IActionMessageScheduler
    {
        public List<Id<CommandEnvelope>> Enqueued { get; } = new();
        public bool ThrowOnEnqueue { get; set; }

        public string? Enqueue(string actionMessageId)
        {
            if (ThrowOnEnqueue)
            {
                throw new InvalidOperationException("Scheduler unavailable");
            }

            if (!Id<CommandEnvelope>.TryParse(actionMessageId, out var parsedActionMessageId))
            {
                throw new FormatException("Action message ID must be a valid ID.");
            }

            Enqueued.Add(parsedActionMessageId);
            return "job-id";
        }
    }

    private sealed class FakeEmailScheduler : IEmailBackgroundScheduler
    {
        public List<Id<NotificationMessage>> Enqueued { get; } = new();
        public bool ThrowOnEnqueue { get; set; }

        public string? Enqueue(string notificationId)
        {
            if (ThrowOnEnqueue)
            {
                throw new InvalidOperationException("Scheduler unavailable");
            }

            if (!Id<NotificationMessage>.TryParse(notificationId, out var parsedNotificationId))
            {
                throw new FormatException("Notification ID must be a valid ID.");
            }

            Enqueued.Add(parsedNotificationId);
            return "job-id";
        }
    }
}
