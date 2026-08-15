using FluentAssertions;
using LgymApi.Application.Notifications;
using LgymApi.Application.Repositories;
using LgymApi.Domain.Entities;
using LgymApi.Domain.Enums;
using LgymApi.Domain.ValueObjects;
using LgymApi.Infrastructure.Data;
using LgymApi.Infrastructure.Repositories;
using LgymApi.Infrastructure.UnitOfWork;
using Microsoft.AspNetCore.Hosting;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;

namespace LgymApi.IntegrationTests;

[TestFixture]
public sealed class PushRetentionCleanupFailureIntegrationTests
{
    [Test]
    public async Task CleanupAsync_WhenSecondBatchCommitFails_PersistsOnlyTheEarlierCompletedBatchInAFreshContext()
    {
        using var baseFactory = new CustomWebApplicationFactory();
        using var factory = baseFactory.WithWebHostBuilder(builder =>
        {
            builder.UseSetting("PushNotifications:MessageHistoryDays", "30");
            builder.UseSetting("PushNotifications:RetentionPurgeBatchSize", "1");
        });
        var firstMessageId = Id<PushNotificationMessage>.New();
        var secondMessageId = Id<PushNotificationMessage>.New();
        var thirdMessageId = Id<PushNotificationMessage>.New();
        var expired = DateTimeOffset.UtcNow.AddDays(-31);

        using (var setupScope = factory.Services.CreateScope())
        {
            var database = setupScope.ServiceProvider.GetRequiredService<AppDbContext>();
            database.PushNotificationMessages.AddRange(
                CreateMessage(firstMessageId, expired),
                CreateMessage(secondMessageId, expired.AddMinutes(1)),
                CreateMessage(thirdMessageId, expired.AddMinutes(2)));
            await database.SaveChangesAsync();
        }

        using (var cleanupScope = factory.Services.CreateScope())
        {
            var database = cleanupScope.ServiceProvider.GetRequiredService<AppDbContext>();
            var cleanup = new PushNotificationMessageRetentionCleanupService(
                new PushNotificationMessageRepository(database),
                new FailOnSaveUnitOfWork(new EfUnitOfWork(database), failingSaveCall: 2),
                cleanupScope.ServiceProvider.GetRequiredService<INotificationRetentionSettings>(),
                NullLogger<PushNotificationMessageRetentionCleanupService>.Instance);

            var action = () => cleanup.CleanupAsync();

            await action.Should().ThrowAsync<InvalidOperationException>()
                .WithMessage("Forced retention batch commit failure.");
        }

        using var verificationScope = factory.Services.CreateScope();
        var verificationDatabase = verificationScope.ServiceProvider.GetRequiredService<AppDbContext>();
        var remainingMessageIds = await verificationDatabase.PushNotificationMessages
            .AsNoTracking()
            .OrderBy(message => message.CreatedAt)
            .Select(message => message.Id)
            .ToListAsync();

        remainingMessageIds.Should().Equal(secondMessageId, thirdMessageId);
        remainingMessageIds.Should().NotContain(firstMessageId);
    }

    private static PushNotificationMessage CreateMessage(
        Id<PushNotificationMessage> id,
        DateTimeOffset createdAt)
        => new()
        {
            Id = id,
            UserId = Id<User>.New(),
            PushInstallationId = Id<PushInstallation>.New(),
            SchemaVersion = 1,
            Type = "retention.failure",
            EventId = id.ToString(),
            PayloadJson = "{}",
            Status = PushNotificationStatus.Sent,
            CreatedAt = createdAt
        };

    private sealed class FailOnSaveUnitOfWork(IUnitOfWork inner, int failingSaveCall) : IUnitOfWork
    {
        private int _saveCalls;

        public Task<int> SaveChangesAsync(CancellationToken cancellationToken = default)
        {
            _saveCalls++;
            return _saveCalls == failingSaveCall
                ? Task.FromException<int>(new InvalidOperationException("Forced retention batch commit failure."))
                : inner.SaveChangesAsync(cancellationToken);
        }

        public Task<IUnitOfWorkTransaction> BeginTransactionAsync(CancellationToken cancellationToken = default)
            => inner.BeginTransactionAsync(cancellationToken);
    }
}
