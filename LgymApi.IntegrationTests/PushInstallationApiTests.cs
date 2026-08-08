using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using System.Text.Json.Serialization;
using FluentAssertions;
using LgymApi.Infrastructure.Data;
using LgymApi.Resources;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace LgymApi.IntegrationTests;

[TestFixture]
public sealed class PushInstallationApiTests : IntegrationTestBase
{
    [Test]
    [LgymApi.IntegrationTests.Authorization.AuthorizationEvidence("POST", "/api/push/installations/unregister", "own", "owner-allow")]
    [LgymApi.IntegrationTests.Authorization.AuthorizationEvidence("POST", "/api/push/installations/register", "own", "owner-allow")]
    [LgymApi.IntegrationTests.Authorization.AuthorizationEvidence("POST", "/api/push/installations/disassociate", "own", "owner-allow")]
    public async Task PushInstallationEndpoints_UseLegacyRoutesJsonFieldsAndMessageResponse()
    {
        var user = await SeedUserAsync(name: "push-wire", email: "push-wire@example.com", password: "push-pass-123");
        SetAuthorizationHeader(user.Id);

        var registerResponse = await Client.PostAsJsonAsync("/api/push/installations/register", new
        {
            installationId = "device-wire",
            platform = "android",
            fcmToken = "token-wire",
            appVersion = "1.0.0",
            environment = "development",
            permissionStatus = "authorized"
        });
        await AssertLegacyMessageResponseAsync(registerResponse);

        var unregisterResponse = await Client.PostAsJsonAsync("/api/push/installations/unregister", new
        {
            installationId = "device-wire"
        });
        await AssertLegacyMessageResponseAsync(unregisterResponse);

        var disassociateResponse = await Client.PostAsJsonAsync("/api/push/installations/disassociate", new
        {
            installationId = "device-wire"
        });
        await AssertLegacyMessageResponseAsync(disassociateResponse);
    }

    [Test]
    public async Task PushInstallationEndpoints_RejectBlankAndAlternateInstallationIdentifiers()
    {
        var user = await SeedUserAsync(name: "push-validation", email: "push-validation@example.com", password: "push-pass-123");
        SetAuthorizationHeader(user.Id);

        var blankRegistration = await Client.PostAsJsonAsync("/api/push/installations/register", new
        {
            installationId = "",
            platform = "android",
            fcmToken = "token-validation",
            environment = "development"
        });
        blankRegistration.StatusCode.Should().Be(HttpStatusCode.BadRequest);

        var alternateRegistration = await Client.PostAsJsonAsync("/api/push/installations/register", new
        {
            installationKey = "device-validation",
            platform = "android",
            fcmToken = "token-validation",
            environment = "development"
        });
        alternateRegistration.StatusCode.Should().Be(HttpStatusCode.BadRequest);

        await Client.PostAsJsonAsync("/api/push/installations/register", new
        {
            installationId = "device-validation",
            platform = "android",
            fcmToken = "token-validation",
            environment = "development"
        });

        var blankUnregister = await Client.PostAsJsonAsync("/api/push/installations/unregister", new { installationId = "" });
        var alternateUnregister = await Client.PostAsJsonAsync("/api/push/installations/unregister", new { installationKey = "device-validation" });
        var blankDisassociate = await Client.PostAsJsonAsync("/api/push/installations/disassociate", new { installationId = "" });
        var alternateDisassociate = await Client.PostAsJsonAsync("/api/push/installations/disassociate", new { installationKey = "device-validation" });

        blankUnregister.StatusCode.Should().Be(HttpStatusCode.BadRequest);
        alternateUnregister.StatusCode.Should().Be(HttpStatusCode.BadRequest);
        blankDisassociate.StatusCode.Should().Be(HttpStatusCode.BadRequest);
        alternateDisassociate.StatusCode.Should().Be(HttpStatusCode.BadRequest);

        using var scope = Factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var installation = await db.PushInstallations.SingleAsync(x => x.InstallationId == "device-validation");
        installation.UserId.Should().Be(user.Id);
        installation.SessionId.Should().NotBeNull();
        installation.DisabledAt.Should().BeNull();
    }

    [Test]
    public async Task RegisterPushInstallation_MissingTokenOrPlatform_ReturnsLocalizedBadRequestWithoutEchoingToken()
    {
        var user = await SeedUserAsync(name: "push-token-platform", email: "push-token-platform@example.com", password: "push-pass-123");
        SetAuthorizationHeader(user.Id);

        var missingToken = await Client.PostAsJsonAsync("/api/push/installations/register", new
        {
            installationId = "device-missing-token",
            platform = "android",
            fcmToken = "",
            environment = "development"
        });
        var missingPlatform = await Client.PostAsJsonAsync("/api/push/installations/register", new
        {
            installationId = "device-missing-platform",
            platform = "",
            fcmToken = "token-that-must-not-leak",
            environment = "development"
        });

        missingToken.StatusCode.Should().Be(HttpStatusCode.BadRequest);
        missingPlatform.StatusCode.Should().Be(HttpStatusCode.BadRequest);
        (await missingToken.Content.ReadAsStringAsync()).Should().Contain("All fields are required.");
        (await missingPlatform.Content.ReadAsStringAsync())
            .Should().Contain("All fields are required.")
            .And.NotContain("token-that-must-not-leak");

        using var scope = Factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        (await db.PushInstallations.CountAsync()).Should().Be(0);
    }

    [Test]
    [LgymApi.IntegrationTests.Authorization.AuthorizationEvidence("POST", "/api/push/installations/unregister", "own", "foreign-object-denial-no-mutation")]
    [LgymApi.IntegrationTests.Authorization.AuthorizationEvidence("POST", "/api/push/installations/disassociate", "own", "foreign-object-denial-no-mutation")]
    public async Task PushInstallationActions_CannotMutateAnotherUsersInstallation()
    {
        var owner = await SeedUserAsync(name: "push-owner", email: "push-owner@example.com", password: "push-pass-123");
        var otherUser = await SeedUserAsync(name: "push-other", email: "push-other@example.com", password: "push-pass-123");
        SetAuthorizationHeader(owner.Id);

        await Client.PostAsJsonAsync("/api/push/installations/register", new
        {
            installationId = "device-owner",
            platform = "ios",
            fcmToken = "token-owner",
            environment = "production"
        });

        using var ownerScope = Factory.Services.CreateScope();
        var ownerDb = ownerScope.ServiceProvider.GetRequiredService<AppDbContext>();
        var ownerInstallation = await ownerDb.PushInstallations.SingleAsync(x => x.InstallationId == "device-owner");
        var ownerSessionId = ownerInstallation.SessionId;

        SetAuthorizationHeader(otherUser.Id);
        var unregisterResponse = await Client.PostAsJsonAsync("/api/push/installations/unregister", new { installationId = "device-owner" });
        var disassociateResponse = await Client.PostAsJsonAsync("/api/push/installations/disassociate", new { installationId = "device-owner" });

        unregisterResponse.StatusCode.Should().Be(HttpStatusCode.OK);
        disassociateResponse.StatusCode.Should().Be(HttpStatusCode.OK);

        using var assertionScope = Factory.Services.CreateScope();
        var assertionDb = assertionScope.ServiceProvider.GetRequiredService<AppDbContext>();
        var installation = await assertionDb.PushInstallations.SingleAsync(x => x.InstallationId == "device-owner");
        installation.UserId.Should().Be(owner.Id);
        installation.SessionId.Should().Be(ownerSessionId);
        installation.DisabledAt.Should().BeNull();
    }

    [Test]
    public async Task RegisterPushInstallation_AfterUnregister_ReactivatesTheSameInstallation()
    {
        var user = await SeedUserAsync(name: "push-reactivate", email: "push-reactivate@example.com", password: "push-pass-123");
        SetAuthorizationHeader(user.Id);

        await Client.PostAsJsonAsync("/api/push/installations/register", new
        {
            installationId = "device-reactivate",
            platform = "android",
            fcmToken = "token-old",
            environment = "development"
        });
        await Client.PostAsJsonAsync("/api/push/installations/unregister", new { installationId = "device-reactivate" });
        var reregisterResponse = await Client.PostAsJsonAsync("/api/push/installations/register", new
        {
            installationId = "device-reactivate",
            platform = "android",
            fcmToken = "token-new",
            environment = "development"
        });

        await AssertLegacyMessageResponseAsync(reregisterResponse);

        using var scope = Factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var installations = await db.PushInstallations.Where(x => x.InstallationId == "device-reactivate").ToListAsync();
        installations.Should().ContainSingle();
        installations[0].UserId.Should().Be(user.Id);
        installations[0].FcmToken.Should().Be("token-new");
        installations[0].DisabledAt.Should().BeNull();
        installations[0].DisabledReason.Should().BeNull();
    }

    [Test]
    public async Task RegisterPushInstallation_WithAuthenticatedUser_CreatesInstallationRow()
    {
        var user = await SeedUserAsync(name: "push-user", email: "push-user@example.com", password: "push-pass-123");
        SetAuthorizationHeader(user.Id);

        var response = await Client.PostAsJsonAsync("/api/push/installations/register", new
        {
            installationId = "device-1",
            platform = "android",
            fcmToken = "token-1",
            appVersion = "1.0.0",
            environment = "development",
            permissionStatus = "authorized"
        });

        response.StatusCode.Should().Be(HttpStatusCode.OK);

        using var scope = Factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var installation = await db.PushInstallations.SingleAsync(x => x.InstallationId == "device-1");

        installation.UserId.Should().Be(user.Id);
        installation.SessionId.Should().NotBeNull();
        installation.Platform.Should().Be("android");
        installation.FcmToken.Should().Be("token-1");
        installation.AppVersion.Should().Be("1.0.0");
        installation.Environment.Should().Be("development");
        installation.PermissionStatus.Should().Be("authorized");
        installation.DisabledAt.Should().BeNull();
        installation.DisabledReason.Should().BeNull();
    }

    [Test]
    public async Task RegisterPushInstallation_WhenRepeatedForSameInstallation_IsIdempotentAndUpdatesExistingRow()
    {
        var user = await SeedUserAsync(name: "push-repeat", email: "push-repeat@example.com", password: "push-pass-123");
        SetAuthorizationHeader(user.Id);

        var firstResponse = await Client.PostAsJsonAsync("/api/push/installations/register", new
        {
            installationId = "device-repeat",
            platform = "ios",
            fcmToken = "token-old",
            appVersion = "1.0.0",
            environment = "production",
            permissionStatus = "provisional"
        });
        firstResponse.StatusCode.Should().Be(HttpStatusCode.OK);

        await Task.Delay(10);

        var secondResponse = await Client.PostAsJsonAsync("/api/push/installations/register", new
        {
            installationId = "device-repeat",
            platform = "ios",
            fcmToken = "token-new",
            appVersion = "1.1.0",
            environment = "production",
            permissionStatus = "authorized"
        });
        secondResponse.StatusCode.Should().Be(HttpStatusCode.OK);

        using var scope = Factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var installations = await db.PushInstallations
            .Where(x => x.InstallationId == "device-repeat")
            .ToListAsync();

        installations.Should().HaveCount(1);
        installations[0].FcmToken.Should().Be("token-new");
        installations[0].AppVersion.Should().Be("1.1.0");
        installations[0].PermissionStatus.Should().Be("authorized");
        installations[0].UpdatedAt.Should().BeAfter(installations[0].CreatedAt);
    }

    [Test]
    public async Task UnregisterPushInstallation_DisablesInstallationWithoutDeletingIt()
    {
        var user = await SeedUserAsync(name: "push-unregister", email: "push-unregister@example.com", password: "push-pass-123");
        SetAuthorizationHeader(user.Id);

        await Client.PostAsJsonAsync("/api/push/installations/register", new
        {
            installationId = "device-unregister",
            platform = "android",
            fcmToken = "token-unregister",
            appVersion = "1.0.0",
            environment = "development"
        });

        var response = await Client.PostAsJsonAsync("/api/push/installations/unregister", new
        {
            installationId = "device-unregister"
        });

        response.StatusCode.Should().Be(HttpStatusCode.OK);

        using var scope = Factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var installation = await db.PushInstallations.SingleAsync(x => x.InstallationId == "device-unregister");
        installation.DisabledAt.Should().NotBeNull();
        installation.DisabledReason.Should().Be("Unregistered");
        installation.UserId.Should().Be(user.Id);
    }

    [Test]
    public async Task DisassociatePushInstallation_ClearsUserAndSessionBinding()
    {
        var user = await SeedUserAsync(name: "push-disassociate", email: "push-disassociate@example.com", password: "push-pass-123");
        SetAuthorizationHeader(user.Id);

        await Client.PostAsJsonAsync("/api/push/installations/register", new
        {
            installationId = "device-disassociate",
            platform = "ios",
            fcmToken = "token-disassociate",
            appVersion = "1.0.0",
            environment = "production"
        });

        var response = await Client.PostAsJsonAsync("/api/push/installations/disassociate", new
        {
            installationId = "device-disassociate"
        });

        response.StatusCode.Should().Be(HttpStatusCode.OK);

        using var scope = Factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var installation = await db.PushInstallations.SingleAsync(x => x.InstallationId == "device-disassociate");
        installation.UserId.Should().BeNull();
        installation.SessionId.Should().BeNull();
        installation.DisabledAt.Should().BeNull();
    }

    [Test]
    public async Task Logout_DisassociatesInstallationsBoundToCurrentSession()
    {
        await SeedUserAsync(name: "push-logout", email: "push-logout@example.com", password: "push-pass-123");
        var login = await LoginAsync("push-logout", "push-pass-123");
        Client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", login.Token);

        await Client.PostAsJsonAsync("/api/push/installations/register", new
        {
            installationId = "device-logout",
            platform = "android",
            fcmToken = "token-logout",
            appVersion = "1.0.0",
            environment = "production"
        });

        var logoutResponse = await Client.PostAsync("/api/logout", null);
        logoutResponse.StatusCode.Should().Be(HttpStatusCode.OK);

        using var scope = Factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var installation = await db.PushInstallations.SingleAsync(x => x.InstallationId == "device-logout");
        installation.UserId.Should().BeNull();
        installation.SessionId.Should().BeNull();
    }

    [Test]
    [LgymApi.IntegrationTests.Authorization.AuthorizationEvidence("POST", "/api/push/installations/unregister", "own", "anonymous-denial")]
    [LgymApi.IntegrationTests.Authorization.AuthorizationEvidence("POST", "/api/push/installations/register", "own", "anonymous-denial")]
    [LgymApi.IntegrationTests.Authorization.AuthorizationEvidence("POST", "/api/push/installations/disassociate", "own", "anonymous-denial")]
    public async Task RegisterPushInstallation_WithoutAuthorization_IsRejectedAndCreatesNoRows()
    {
        var response = await Client.PostAsJsonAsync("/api/push/installations/register", new
        {
            installationId = "device-anon",
            platform = "android",
            fcmToken = "token-anon",
            appVersion = "1.0.0",
            environment = "development"
        });

        response.StatusCode.Should().Be(HttpStatusCode.Unauthorized);
        using (var body = JsonDocument.Parse(await response.Content.ReadAsStringAsync()))
        {
            body.RootElement.GetProperty("msg").GetString().Should().Be(
                CompatibilityResourceMessage.InCulture("en", () => Messages.InvalidToken));
            body.RootElement.TryGetProperty("message", out _).Should().BeFalse();
        }

        using var scope = Factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        (await db.PushInstallations.CountAsync()).Should().Be(0);
    }

    private async Task<LoginResponse> LoginAsync(string name, string password)
    {
        var response = await Client.PostAsJsonAsync("/api/login", new { name, password });
        response.StatusCode.Should().Be(HttpStatusCode.OK);

        var body = await response.Content.ReadFromJsonAsync<LoginResponse>();
        body.Should().NotBeNull();
        return body!;
    }

    private static async Task AssertLegacyMessageResponseAsync(HttpResponseMessage response)
    {
        response.StatusCode.Should().Be(HttpStatusCode.OK);

        using var document = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        document.RootElement.TryGetProperty("msg", out var message).Should().BeTrue();
        message.ValueKind.Should().Be(JsonValueKind.String);
        document.RootElement.TryGetProperty("message", out _).Should().BeFalse();
    }

    private sealed class LoginResponse
    {
        [JsonPropertyName("token")]
        public string Token { get; set; } = string.Empty;
    }
}
