using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json.Serialization;
using FluentAssertions;
using LgymApi.Application.Notifications;
using LgymApi.Application.Notifications.Models;
using LgymApi.BackgroundWorker.Push;
using LgymApi.Domain.Entities;
using LgymApi.Domain.Enums;
using LgymApi.Domain.ValueObjects;
using LgymApi.Infrastructure.Data;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace LgymApi.IntegrationTests;

[TestFixture]
public sealed class PushNotificationInstallationIsolationIntegrationTests : IntegrationTestBase
{
    [Test]
    public async Task QueuedNotification_WhenInstallationIsReboundAfterLogout_DeliversOnlyTheNewUsersEvent()
    {
        const string installationKey = "shared-installation";
        const string aEventId = "event-owned-by-a";
        const string bEventId = "event-owned-by-b";
        var userA = await SeedUserAsync("push-isolation-a", "push-isolation-a@example.com", "pass123");
        var userB = await SeedUserAsync("push-isolation-b", "push-isolation-b@example.com", "pass123");

        await LoginAndAuthorizeAsync("push-isolation-a", "pass123");
        var registerAResponse = await Client.PostAsJsonAsync("/api/push/installations/register", new
        {
            installationId = installationKey,
            platform = "android",
            fcmToken = "token-owned-by-a",
            environment = "testing",
            permissionStatus = "authorized"
        });
        registerAResponse.StatusCode.Should().Be(HttpStatusCode.OK);

        Id<PushInstallation> installationId;
        Id<PushNotificationMessage> aMessageId;
        using (var scope = Factory.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
            var installation = await db.PushInstallations.SingleAsync(item => item.InstallationId == installationKey);
            installationId = installation.Id;
            installation.UserId.Should().Be(userA.Id);

            var pushNotifications = scope.ServiceProvider.GetRequiredService<IPushNotificationService>();
            await pushNotifications.EnqueueAsync(new EnqueuePushNotificationInput(
                userA.Id,
                1,
                "push.installation.isolation",
                aEventId,
                null,
                null,
                null));
            aMessageId = await db.PushNotificationMessages
                .Where(message => message.EventId == aEventId)
                .Select(message => message.Id)
                .SingleAsync();
        }

        var logoutResponse = await Client.PostAsync("/api/logout", null);
        logoutResponse.StatusCode.Should().Be(HttpStatusCode.OK);

        using (var scope = Factory.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
            var loggedOutInstallation = await db.PushInstallations.SingleAsync(item => item.InstallationId == installationKey);
            loggedOutInstallation.Id.Should().Be(installationId);
            loggedOutInstallation.UserId.Should().BeNull();
            loggedOutInstallation.SessionId.Should().BeNull();
        }

        await LoginAndAuthorizeAsync("push-isolation-b", "pass123");
        var registerBResponse = await Client.PostAsJsonAsync("/api/push/installations/register", new
        {
            installationId = installationKey,
            platform = "android",
            fcmToken = "token-owned-by-b",
            environment = "testing",
            permissionStatus = "authorized"
        });
        registerBResponse.StatusCode.Should().Be(HttpStatusCode.OK);

        Id<PushNotificationMessage> bMessageId;
        using (var scope = Factory.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
            var reboundInstallation = await db.PushInstallations.SingleAsync(item => item.InstallationId == installationKey);
            reboundInstallation.Id.Should().Be(installationId);
            reboundInstallation.UserId.Should().Be(userB.Id);
            reboundInstallation.SessionId.Should().NotBeNull();

            var pushNotifications = scope.ServiceProvider.GetRequiredService<IPushNotificationService>();
            await pushNotifications.EnqueueAsync(new EnqueuePushNotificationInput(
                userB.Id,
                1,
                "push.installation.isolation",
                bEventId,
                null,
                null,
                null));
            bMessageId = await db.PushNotificationMessages
                .Where(message => message.EventId == bEventId)
                .Select(message => message.Id)
                .SingleAsync();
        }

        using (var scope = Factory.Services.CreateScope())
        {
            var handler = scope.ServiceProvider.GetRequiredService<PushNotificationJobHandlerService>();
            await handler.ProcessAsync(aMessageId.ToString());
            await handler.ProcessAsync(bMessageId.ToString());
        }

        Factory.PushSender.Attempts.Should().ContainSingle();
        Factory.PushSender.Attempts.Single().InstallationId.Should().Be(installationId);
        Factory.PushSender.Attempts.Single().Payload.EventId.Should().Be(bEventId);

        using (var scope = Factory.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
            var aMessage = await db.PushNotificationMessages.SingleAsync(message => message.Id == aMessageId);
            var bMessage = await db.PushNotificationMessages.SingleAsync(message => message.Id == bMessageId);
            aMessage.Status.Should().Be(PushNotificationStatus.Failed);
            aMessage.FailureKind.Should().Be(PushNotificationFailureKind.Permanent);
            aMessage.ProviderStatus.Should().Be("InstallationUserMismatch");
            aMessage.NextAttemptAt.Should().BeNull();
            aMessage.SchedulerJobId.Should().BeNull();
            bMessage.Status.Should().Be(PushNotificationStatus.Skipped);
        }
    }

    private async Task LoginAndAuthorizeAsync(string name, string password)
    {
        var response = await Client.PostAsJsonAsync("/api/login", new { name, password });
        response.StatusCode.Should().Be(HttpStatusCode.OK);
        var login = await response.Content.ReadFromJsonAsync<LoginResponse>();
        login.Should().NotBeNull();
        Client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", login!.Token);
    }

    private sealed class LoginResponse
    {
        [JsonPropertyName("token")]
        public string Token { get; set; } = string.Empty;
    }
}
