using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using FluentAssertions;
using LgymApi.Infrastructure.Data;
using Microsoft.EntityFrameworkCore;
using LgymApi.Domain.Entities;

namespace LgymApi.IntegrationTests;

[TestFixture]
public sealed class AgeGateTests : IntegrationTestBase
{
    protected override CustomWebApplicationFactory CreateFactory() => new(ageGateEnabled: true);

    [Test]
    [LgymApi.IntegrationTests.Authorization.AuthorizationEvidence("POST", "/api/account/confirm-adult", "own", "anonymous-denial")]
    public async Task AnonymousUser_CannotConfirmAdult()
    {
        var response = await PostAsJsonWithApiOptionsAsync(
            "/api/account/confirm-adult",
            new { adultConfirmed = true });

        response.StatusCode.Should().Be(HttpStatusCode.Unauthorized);
    }

    [TestCase(false)]
    [TestCase(null)]
    public async Task ConfirmAdult_RejectsMissingOrFalseConfirmation(bool? adultConfirmed)
    {
        var user = await SeedUserAsync(
            $"invalid-confirmation-{adultConfirmed?.ToString() ?? "missing"}",
            $"invalid-confirmation-{adultConfirmed?.ToString() ?? "missing"}@example.com");
        SetAuthorizationHeader(user.Id);

        var response = await PostAsJsonWithApiOptionsAsync(
            "/api/account/confirm-adult",
            new { adultConfirmed });

        response.StatusCode.Should().Be(HttpStatusCode.BadRequest);
        await using var db = GetDbContext();
        var persisted = await db.Users.SingleAsync(candidate => candidate.Id == user.Id);
        persisted.AdultConfirmedAt.Should().BeNull();
        persisted.AdultConfirmationVersion.Should().BeNull();
    }

    [Test]
    public async Task PasswordLogin_UnconfirmedAccount_ReturnsAdultConfirmationRequired()
    {
        await SeedUserAsync("password-gated", "password-gated@example.com");

        var response = await Client.PostAsJsonAsync(
            "/api/login",
            new { name = "password-gated", password = "password123" });

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        var payload = await response.Content.ReadFromJsonAsync<JsonElement>();
        payload.GetProperty("adultConfirmationRequired").GetBoolean().Should().BeTrue();
        payload.GetProperty("req").GetProperty("adultConfirmationRequired").GetBoolean().Should().BeTrue();
    }

    [Test]
    public async Task TrainerLogin_UnconfirmedAccount_ReturnsAdultConfirmationRequired()
    {
        var trainer = await SeedUserAsync("trainer-gated", "trainer-gated@example.com");
        await using (var db = GetDbContext())
        {
            var trainerRole = await db.Roles.SingleAsync(role => role.Name == "Trainer");
            db.UserRoles.Add(new UserRole
            {
                UserId = trainer.Id,
                RoleId = trainerRole.Id
            });
            await db.SaveChangesAsync();
        }

        var response = await Client.PostAsJsonAsync(
            "/api/trainer/login",
            new { name = "trainer-gated", password = "password123" });

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        var payload = await response.Content.ReadFromJsonAsync<JsonElement>();
        payload.GetProperty("adultConfirmationRequired").GetBoolean().Should().BeTrue();
        payload.GetProperty("req").GetProperty("adultConfirmationRequired").GetBoolean().Should().BeTrue();
    }

    [Test]
    [LgymApi.IntegrationTests.Authorization.AuthorizationEvidence("POST", "/api/account/confirm-adult", "own", "owner-allow")]
    [LgymApi.IntegrationTests.Authorization.AuthorizationEvidence("POST", "/api/account/confirm-adult", "own", "no-client-subject")]
    public async Task ExistingSession_IsGatedUntilIdempotentAdultConfirmationSucceeds()
    {
        var user = await SeedUserAsync();
        user.AdultConfirmedAt.Should().BeNull();
        user.AdultConfirmationVersion.Should().BeNull();
        SetAuthorizationHeader(user.Id);

        var blockedResponse = await Client.GetAsync("/api/getUsersRanking");
        blockedResponse.StatusCode.Should().Be(HttpStatusCode.PreconditionRequired);
        var blockedPayload = await blockedResponse.Content.ReadFromJsonAsync<JsonElement>();
        blockedPayload.GetProperty("code").GetString().Should().Be("AdultConfirmationRequired");

        var blockedHub = await Client.PostAsync(
            "/hubs/notifications/negotiate?negotiateVersion=1",
            null);
        blockedHub.StatusCode.Should().Be(HttpStatusCode.PreconditionRequired);

        var bootstrapResponse = await Client.GetAsync("/api/checkToken");
        bootstrapResponse.StatusCode.Should().Be(HttpStatusCode.OK);
        var bootstrapPayload = await bootstrapResponse.Content.ReadFromJsonAsync<JsonElement>();
        bootstrapPayload.GetProperty("adultConfirmationRequired").GetBoolean().Should().BeTrue();

        var firstConfirmation = await PostAsJsonWithApiOptionsAsync(
            "/api/account/confirm-adult",
            new { adultConfirmed = true });
        firstConfirmation.StatusCode.Should().Be(HttpStatusCode.OK);

        var repeatedConfirmation = await PostAsJsonWithApiOptionsAsync(
            "/api/account/confirm-adult",
            new { adultConfirmed = true });
        repeatedConfirmation.StatusCode.Should().Be(HttpStatusCode.OK);

        var allowedResponse = await Client.GetAsync("/api/getUsersRanking");
        allowedResponse.StatusCode.Should().Be(HttpStatusCode.OK);
        var allowedHub = await Client.PostAsync(
            "/hubs/notifications/negotiate?negotiateVersion=1",
            null);
        allowedHub.StatusCode.Should().Be(HttpStatusCode.OK);

        await using var db = GetDbContext();
        var persisted = await db.Users.SingleAsync(candidate => candidate.Id == user.Id);
        persisted.AdultConfirmedAt.Should().NotBeNull();
        persisted.AdultConfirmationVersion.Should().Be("18plus-v1");
    }

    [Test]
    public async Task AgeGatedAccount_CanLogoutAndDeleteAccount()
    {
        var logoutUser = await SeedUserAsync("logout-user", "logout@example.com");
        SetAuthorizationHeader(logoutUser.Id);
        (await Client.PostAsync("/api/logout", null)).StatusCode.Should().Be(HttpStatusCode.OK);

        var deleteUser = await SeedUserAsync("delete-user", "delete@example.com");
        SetAuthorizationHeader(deleteUser.Id);
        (await Client.GetAsync("/api/deleteAccount")).StatusCode.Should().Be(HttpStatusCode.OK);
    }

    [TestCase(false)]
    [TestCase(null)]
    public async Task StandardRegistration_RejectsMissingOrFalseAdultConfirmation(bool? adultConfirmed)
    {
        var response = await PostAsJsonWithApiOptionsAsync("/api/register", new
        {
            name = $"rejected-{adultConfirmed?.ToString() ?? "missing"}",
            email = $"rejected-{adultConfirmed?.ToString() ?? "missing"}@example.com",
            password = "password123",
            cpassword = "password123",
            isVisibleInRanking = true,
            adultConfirmed
        });

        response.StatusCode.Should().Be(HttpStatusCode.BadRequest);
    }

    [TestCase("/api/register", false)]
    [TestCase("/api/trainer/register", true)]
    public async Task Registration_WithAdultConfirmation_PersistsTimestampAndVersion(string route, bool trainer)
    {
        var suffix = trainer ? "trainer" : "user";
        var response = await PostAsJsonWithApiOptionsAsync(route, new
        {
            name = $"confirmed-{suffix}",
            email = $"confirmed-{suffix}@example.com",
            password = "password123",
            cpassword = "password123",
            isVisibleInRanking = true,
            adultConfirmed = true
        });

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        await using var db = GetDbContext();
        var persisted = await db.Users.SingleAsync(user => user.Email == $"confirmed-{suffix}@example.com");
        persisted.AdultConfirmedAt.Should().NotBeNull();
        persisted.AdultConfirmationVersion.Should().Be("18plus-v1");
    }
}
