using FluentAssertions;
using LgymApi.Application.Features.Tutorial;
using LgymApi.Application.Identity.Contracts.Accounts;
using LgymApi.Application.Identity.Profile;
using LgymApi.Application.Mapping.Core;
using LgymApi.Application.Notifications;
using LgymApi.Application.Notifications.Adapters;
using LgymApi.Application.Notifications.Repositories;
using LgymApi.Application.Options;
using LgymApi.Application.Repositories;
using LgymApi.Application.Services;
using LgymApi.Domain.Entities;
using LgymApi.Domain.Enums;
using LgymApi.Domain.ValueObjects;
using LgymApi.Infrastructure.Data;
using LgymApi.Infrastructure.Repositories;
using LgymApi.Infrastructure.UnitOfWork;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace LgymApi.IntegrationTests;

[TestFixture]
[Category("PostgreSql")]
internal sealed class PostgreSqlAccountDeletionSharedUnitOfWorkTests : PostgreSqlIntegrationTestBase
{
    [Test]
    public async Task DeleteAccountAsync_StagesAnonymizationAndPushRemovalUntilOneSharedCommit()
    {
        var user = await SeedUserAsync(
            $"postgres-shared-delete-{Id<PostgreSqlAccountDeletionSharedUnitOfWorkTests>.New():N}",
            $"postgres-shared-delete-{Id<PostgreSqlAccountDeletionSharedUnitOfWorkTests>.New():N}@example.test");
        var installationId = Id<PushInstallation>.New();
        var messageId = Id<PushNotificationMessage>.New();
        await SeedPushDataAsync(user.Id, installationId, messageId);
        var writesWereInvisibleBeforeCommit = false;

        await using (var serviceScope = Factory.Services.CreateAsyncScope())
        {
            var database = serviceScope.ServiceProvider.GetRequiredService<AppDbContext>();
            var scopedUser = await database.Users.SingleAsync(candidate => candidate.Id == user.Id);
            var unitOfWork = new ObservedUnitOfWork(new EfUnitOfWork(database), async () =>
            {
                var beforeCommit = await ReadStateAsync(user.Id, installationId, messageId);
                writesWereInvisibleBeforeCommit = !beforeCommit.IsDeleted
                    && beforeCommit.InstallationExists
                    && beforeCommit.MessageExists;
            });
            var profileService = new UserProfileService(
                serviceScope.ServiceProvider.GetRequiredService<IUserRepository>(),
                serviceScope.ServiceProvider.GetRequiredService<IRoleRepository>(),
                serviceScope.ServiceProvider.GetRequiredService<IRankService>(),
                unitOfWork,
                new PushInstallationAccountCleanupAdapter(new PushInstallationLifecycleService(
                    new PushInstallationRepository(database),
                    unitOfWork)),
                new AppDefaultsOptions(),
                serviceScope.ServiceProvider.GetRequiredService<ITutorialService>(),
                serviceScope.ServiceProvider.GetRequiredService<IMapper>());

            var result = await profileService.DeleteAccountAsync(scopedUser);

            result.IsSuccess.Should().BeTrue();
            unitOfWork.SaveChangesCalls.Should().Be(1);
            unitOfWork.BeginTransactionCalls.Should().Be(0);
        }

        writesWereInvisibleBeforeCommit.Should().BeTrue();
        var persisted = await ReadStateAsync(user.Id, installationId, messageId);
        persisted.IsDeleted.Should().BeTrue();
        persisted.InstallationExists.Should().BeFalse();
        persisted.MessageExists.Should().BeFalse();
    }

    private async Task SeedPushDataAsync(
        Id<User> userId,
        Id<PushInstallation> installationId,
        Id<PushNotificationMessage> messageId)
    {
        await using var setupScope = Factory.Services.CreateAsyncScope();
        var database = setupScope.ServiceProvider.GetRequiredService<AppDbContext>();
        database.PushInstallations.Add(new PushInstallation
        {
            Id = installationId,
            UserId = userId,
            InstallationId = $"postgres-shared-delete-{installationId}",
            Platform = "android",
            FcmToken = "postgres-shared-delete-token",
            Environment = "test",
            LastSeenAt = DateTimeOffset.UtcNow
        });
        database.PushNotificationMessages.Add(new PushNotificationMessage
        {
            Id = messageId,
            UserId = userId,
            PushInstallationId = installationId,
            SchemaVersion = 1,
            Type = "account.delete",
            EventId = "postgres-shared-delete-event",
            PayloadJson = "{}",
            Status = PushNotificationStatus.Sent
        });
        await database.SaveChangesAsync();
    }

    private async Task<AccountDeletionState> ReadStateAsync(
        Id<User> userId,
        Id<PushInstallation> installationId,
        Id<PushNotificationMessage> messageId)
    {
        await using var verificationScope = Factory.Services.CreateAsyncScope();
        var database = verificationScope.ServiceProvider.GetRequiredService<AppDbContext>();
        var isDeleted = await database.Users
            .IgnoreQueryFilters()
            .AsNoTracking()
            .Where(user => user.Id == userId)
            .Select(user => user.IsDeleted)
            .SingleAsync();
        var installationExists = await database.PushInstallations
            .AsNoTracking()
            .AnyAsync(installation => installation.Id == installationId);
        var messageExists = await database.PushNotificationMessages
            .AsNoTracking()
            .AnyAsync(message => message.Id == messageId);
        return new AccountDeletionState(isDeleted, installationExists, messageExists);
    }

    private sealed record AccountDeletionState(bool IsDeleted, bool InstallationExists, bool MessageExists);
}
