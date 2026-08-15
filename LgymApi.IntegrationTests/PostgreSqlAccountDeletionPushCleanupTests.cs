using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using FluentAssertions;
using LgymApi.Domain.Entities;
using LgymApi.Domain.Enums;
using LgymApi.Domain.ValueObjects;
using LgymApi.Infrastructure.Data;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace LgymApi.IntegrationTests;

[TestFixture]
[Category("PostgreSql")]
internal sealed class PostgreSqlAccountDeletionPushCleanupTests : PostgreSqlIntegrationTestBase
{
    [Test]
    public async Task DeleteAccount_PreservesLegacyResponseAndCascadesOnlyTheDeletedAccountsPushData()
    {
        var user = await SeedUserAsync(
            $"postgres-delete-push-{Id<PostgreSqlAccountDeletionPushCleanupTests>.New():N}",
            $"postgres-delete-push-{Id<PostgreSqlAccountDeletionPushCleanupTests>.New():N}@example.test");
        var activeInstallationId = Id<PushInstallation>.New();
        var disabledInstallationId = Id<PushInstallation>.New();
        var softDeletedInstallationId = Id<PushInstallation>.New();
        var unrelatedInstallationId = Id<PushInstallation>.New();
        var activeMessageId = Id<PushNotificationMessage>.New();
        var disabledMessageId = Id<PushNotificationMessage>.New();
        var softDeletedMessageId = Id<PushNotificationMessage>.New();
        var unrelatedMessageId = Id<PushNotificationMessage>.New();

        await SeedPushDataAsync(
            user,
            activeInstallationId,
            disabledInstallationId,
            unrelatedInstallationId,
            softDeletedInstallationId,
            activeMessageId,
            disabledMessageId,
            softDeletedMessageId,
            unrelatedMessageId);
        await AuthenticateAsync(user);

        var response = await Client.GetAsync("/api/deleteAccount");

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        using (var responseJson = JsonDocument.Parse(await response.Content.ReadAsStringAsync()))
        {
            responseJson.RootElement.TryGetProperty("msg", out var message).Should().BeTrue();
            message.GetString().Should().Be("Deleted.");
            responseJson.RootElement.TryGetProperty("message", out _).Should().BeFalse();
        }

        await using var verificationScope = Factory.Services.CreateAsyncScope();
        var database = verificationScope.ServiceProvider.GetRequiredService<AppDbContext>();
        var deletedUser = await database.Users
            .IgnoreQueryFilters()
            .AsNoTracking()
            .SingleAsync(candidate => candidate.Id == user.Id);
        var remainingInstallationIds = await database.PushInstallations
            .AsNoTracking()
            .Select(installation => installation.Id)
            .ToListAsync();
        var remainingMessageIds = await database.PushNotificationMessages
            .AsNoTracking()
            .Select(message => message.Id)
            .ToListAsync();

        deletedUser.IsDeleted.Should().BeTrue();
        deletedUser.Name.Should().Be($"anonymized_user_{user.Id}");
        deletedUser.Email.Value.Should().Be($"anonymized_{user.Id}@example.com");
        remainingInstallationIds.Should().Contain(unrelatedInstallationId)
            .And.NotContain(activeInstallationId)
            .And.NotContain(disabledInstallationId)
            .And.NotContain(softDeletedInstallationId);
        remainingMessageIds.Should().Contain(unrelatedMessageId)
            .And.NotContain(activeMessageId)
            .And.NotContain(disabledMessageId)
            .And.NotContain(softDeletedMessageId);
    }

    private async Task SeedPushDataAsync(
        User user,
        Id<PushInstallation> activeInstallationId,
        Id<PushInstallation> disabledInstallationId,
        Id<PushInstallation> softDeletedInstallationId,
        Id<PushInstallation> unrelatedInstallationId,
        Id<PushNotificationMessage> activeMessageId,
        Id<PushNotificationMessage> disabledMessageId,
        Id<PushNotificationMessage> softDeletedMessageId,
        Id<PushNotificationMessage> unrelatedMessageId)
    {
        var unrelatedUser = new User
        {
            Id = Id<User>.New(),
            Name = $"postgres-unrelated-{Id<User>.New():N}",
            Email = $"postgres-unrelated-{Id<User>.New():N}@example.test",
            ProfileRank = "Junior 1"
        };

        await using var setupScope = Factory.Services.CreateAsyncScope();
        var database = setupScope.ServiceProvider.GetRequiredService<AppDbContext>();
        database.Users.Add(unrelatedUser);
        database.PushInstallations.AddRange(
            CreateInstallation(activeInstallationId, user.Id, null),
            CreateInstallation(disabledInstallationId, user.Id, DateTimeOffset.UtcNow.AddDays(-1)),
            CreateInstallation(softDeletedInstallationId, user.Id, null, true),
            CreateInstallation(unrelatedInstallationId, unrelatedUser.Id, null));
        database.PushNotificationMessages.AddRange(
            CreateMessage(activeMessageId, activeInstallationId, user.Id, "postgres-account-delete-active"),
            CreateMessage(disabledMessageId, disabledInstallationId, user.Id, "postgres-account-delete-disabled"),
            CreateMessage(softDeletedMessageId, softDeletedInstallationId, user.Id, "postgres-account-delete-soft-deleted"),
            CreateMessage(unrelatedMessageId, unrelatedInstallationId, unrelatedUser.Id, "postgres-account-delete-unrelated"));
        await database.SaveChangesAsync();
    }

    private async Task AuthenticateAsync(User user)
    {
        var loginResponse = await Client.PostAsJsonAsync("/api/login", new { name = user.Name, password = "password123" });
        loginResponse.StatusCode.Should().Be(HttpStatusCode.OK);
        using var loginJson = JsonDocument.Parse(await loginResponse.Content.ReadAsStringAsync());
        loginJson.RootElement.TryGetProperty("token", out var token).Should().BeTrue();
        Client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", token.GetString());
    }

    private static PushInstallation CreateInstallation(
        Id<PushInstallation> id,
        Id<User> userId,
        DateTimeOffset? disabledAt,
        bool isDeleted = false)
        => new()
        {
            Id = id,
            UserId = userId,
            InstallationId = id.ToString(),
            Platform = "android",
            FcmToken = $"postgres-token-{id}",
            Environment = "test",
            LastSeenAt = disabledAt ?? DateTimeOffset.UtcNow,
            DisabledAt = disabledAt,
            DisabledReason = disabledAt == null ? null : "InactiveStale",
            IsDeleted = isDeleted
        };

    private static PushNotificationMessage CreateMessage(
        Id<PushNotificationMessage> id,
        Id<PushInstallation> installationId,
        Id<User> userId,
        string eventId)
        => new()
        {
            Id = id,
            UserId = userId,
            PushInstallationId = installationId,
            SchemaVersion = 1,
            Type = "account.delete",
            EventId = eventId,
            PayloadJson = "{}",
            Status = PushNotificationStatus.Sent
        };
}
