using System.Net;
using System.Net.Http.Json;
using System.Text.Json.Serialization;
using FluentAssertions;
using LgymApi.BackgroundWorker.Common.Notifications;
using LgymApi.Application.Platform.Contracts.Serialization;
using LgymApi.Domain.Notifications;
using LgymApi.Infrastructure.Data;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace LgymApi.IntegrationTests;

[TestFixture]
public sealed class UserAuthTests : IntegrationTestBase
{
    [Test]
    [LgymApi.IntegrationTests.Authorization.AuthorizationEvidence("POST", "/api/register", "public", "anonymous-intended-behavior")]
    public async Task Register_WithValidData_ReturnsCreatedAndCreatesUserWithElo()
    {
        var request = new
        {
            name = "newuser",
            email = "newuser@example.com",
            password = "password123",
            cpassword = "password123",
            isVisibleInRanking = true
        };

        SetIdempotencyKey("test-register-newuser");
        var response = await Client.PostAsJsonAsync("/api/register", request);
        ClearIdempotencyKey();

        response.StatusCode.Should().Be(HttpStatusCode.OK);

        var body = await response.Content.ReadFromJsonAsync<MessageResponse>();
        body.Should().NotBeNull();
        body!.Message.Should().Be("Created");

        using var scope = Factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();

        var user = await db.Users.FirstOrDefaultAsync(u => u.Name == "newuser");
        user.Should().NotBeNull();
        user!.Email.Value.Should().Be("newuser@example.com");
        user.ProfileRank.Should().Be("Junior 1");
        user.IsVisibleInRanking.Should().BeTrue();

        var eloEntry = await db.EloRegistries.FirstOrDefaultAsync(e => e.UserId == user.Id);
        eloEntry.Should().NotBeNull();
        eloEntry!.Elo.Value.Should().Be(1000);
    }

    [Test]
    public async Task Register_WithNoLanguageSpecified_SendsEnglishWelcomeEmail()
    {
        var request = new
        {
            name = "bob-en",
            email = "bob-en@example.com",
            password = "securepass",
            cpassword = "securepass",
            isVisibleInRanking = true
        };

        SetIdempotencyKey("test-register-bob-en");
        var response = await Client.PostAsJsonAsync("/api/register", request);
        ClearIdempotencyKey();

        response.StatusCode.Should().Be(HttpStatusCode.OK);

        await ProcessPendingCommandsAsync();

        using var scope = Factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var user = await db.Users.FirstOrDefaultAsync(u => u.Name == "bob-en");
        user.Should().NotBeNull();
        user!.PreferredLanguage.Should().StartWith("en");

        var emailLog = await db.NotificationMessages
            .OrderByDescending(e => e.SentAt ?? e.LastAttemptAt)
            .FirstOrDefaultAsync(e => e.Recipient == "bob-en@example.com" && e.Type == EmailNotificationTypes.Welcome);
        emailLog.Should().NotBeNull();

        var payload = System.Text.Json.JsonSerializer.Deserialize<LgymApi.BackgroundWorker.Common.Notifications.Models.WelcomeEmailPayload>(emailLog!.PayloadJson, SharedSerializationOptions.Current);
        payload.Should().NotBeNull();
        payload!.CultureName.Should().StartWith("en");
        emailLog.PayloadJson.Should().Contain("en");
    }
    [Test]
    public async Task Register_WithAcceptLanguage_Header_PlSendsPolishWelcomeEmail()
    {
        var request = new
        {
            name = "alicja-hdr",
            email = "alicja-hdr@example.com",
            password = "securepass",
            cpassword = "securepass",
            isVisibleInRanking = true
        };

        var msg = new System.Net.Http.HttpRequestMessage(System.Net.Http.HttpMethod.Post, "/api/register")
        {
            Content = System.Net.Http.Json.JsonContent.Create(request)
        };
        msg.Headers.Add("Accept-Language", "pl;q=1.0");
        msg.Headers.Add("Idempotency-Key", "test-register-alicja-hdr");

        var response = await Client.SendAsync(msg);
        response.StatusCode.Should().Be(HttpStatusCode.OK);

        await ProcessPendingCommandsAsync();

        using var scope = Factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var user = await db.Users.FirstOrDefaultAsync(u => u.Name == "alicja-hdr");
        user.Should().NotBeNull();
        user!.PreferredLanguage.Should().StartWith("pl");

        var emailLog = await db.NotificationMessages
            .OrderByDescending(e => e.SentAt ?? e.LastAttemptAt)
            .FirstOrDefaultAsync(e => e.Recipient == "alicja-hdr@example.com" && e.Type == EmailNotificationTypes.Welcome);
        emailLog.Should().NotBeNull();

        var payload = System.Text.Json.JsonSerializer.Deserialize<LgymApi.BackgroundWorker.Common.Notifications.Models.WelcomeEmailPayload>(emailLog!.PayloadJson, SharedSerializationOptions.Current);
        payload.Should().NotBeNull();
        payload!.CultureName.Should().StartWith("pl");

        // The Polish template has a distinct subject and body.
        emailLog.PayloadJson.Should().Contain("pl");
        // Optionally check the subject/body as needed.
    }
    [Test]
    public async Task Register_WithPreferredLanguage_PlSendsPolishWelcomeEmail()
    {
        var request = new
        {
            name = "alicja-pl",
            email = "alicja-pl@example.com",
            password = "securepass",
            cpassword = "securepass",
            isVisibleInRanking = true
        };

        var msg = new System.Net.Http.HttpRequestMessage(System.Net.Http.HttpMethod.Post, "/api/register")
        {
            Content = System.Net.Http.Json.JsonContent.Create(request)
        };
        msg.Headers.Add("Accept-Language", "pl-PL;q=1.0");
        msg.Headers.Add("Idempotency-Key", "test-register-alicja-pl");

        var response = await Client.SendAsync(msg);
        response.StatusCode.Should().Be(HttpStatusCode.OK);

        await ProcessPendingCommandsAsync();

        using var scope = Factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var user = await db.Users.FirstOrDefaultAsync(u => u.Name == "alicja-pl");
        user.Should().NotBeNull();
        user!.PreferredLanguage.Should().Be("pl-PL");

        var emailLog = await db.NotificationMessages
            .OrderByDescending(e => e.SentAt ?? e.LastAttemptAt)
            .FirstOrDefaultAsync(e => e.Recipient == "alicja-pl@example.com" && e.Type == EmailNotificationTypes.Welcome);
        emailLog.Should().NotBeNull();

        var payload = System.Text.Json.JsonSerializer.Deserialize<LgymApi.BackgroundWorker.Common.Notifications.Models.WelcomeEmailPayload>(emailLog!.PayloadJson, SharedSerializationOptions.Current);
        payload.Should().NotBeNull();
        payload!.CultureName.Should().Be("pl-PL");

        // The Polish template has a distinct subject and body.
        emailLog.PayloadJson.Should().Contain("pl-PL");
        // Optionally check the subject/body if stored; else, template test ensures content.
    }
    [TestCase("", "test@example.com", "password123", "password123", TestName = "Register_WithEmptyName_ReturnsError")]
    [TestCase("testuser", "invalid-email", "password123", "password123", TestName = "Register_WithInvalidEmail_ReturnsError")]
    [TestCase("testuser", "test@example.com", "12345", "12345", TestName = "Register_WithShortPassword_ReturnsError")]
    [TestCase("testuser", "test@example.com", "password123", "differentpassword", TestName = "Register_WithMismatchedPasswords_ReturnsError")]
    public async Task Register_WithInvalidData_ReturnsBadRequest(string name, string email, string password, string cpassword)
    {
        var request = new
        {
            name,
            email,
            password,
            cpassword
        };

        SetIdempotencyKey($"test-register-invalid-{name}-{email}");
        var response = await Client.PostAsJsonAsync("/api/register", request);
        ClearIdempotencyKey();

        response.StatusCode.Should().Be(HttpStatusCode.BadRequest);
    }

    [Test]
    public async Task Register_WithExistingName_ReturnsConflict()
    {
        await SeedUserAsync(name: "existinguser", email: "existing@example.com");

        var request = new
        {
            name = "existinguser",
            email = "new@example.com",
            password = "password123",
            cpassword = "password123"
        };

        SetIdempotencyKey("test-register-existingname");
        var response = await Client.PostAsJsonAsync("/api/register", request);
        ClearIdempotencyKey();

        response.StatusCode.Should().Be(HttpStatusCode.Conflict);

        var body = await response.Content.ReadFromJsonAsync<MessageResponse>();
        body.Should().NotBeNull();
        body!.Message.Should().Be("A user with that name already exists.");
    }

    [Test]
    public async Task Register_WithExistingEmail_ReturnsConflict()
    {
        await SeedUserAsync(name: "existinguser", email: "existing@example.com");

        var request = new
        {
            name = "newuser",
            email = "existing@example.com",
            password = "password123",
            cpassword = "password123"
        };

        SetIdempotencyKey("test-register-existingemail");
        var response = await Client.PostAsJsonAsync("/api/register", request);
        ClearIdempotencyKey();

        response.StatusCode.Should().Be(HttpStatusCode.Conflict);

        var body = await response.Content.ReadFromJsonAsync<MessageResponse>();
        body.Should().NotBeNull();
        body!.Message.Should().Be("A user with that email already exists.");
    }

    [Test]
    [LgymApi.IntegrationTests.Authorization.AuthorizationEvidence("POST", "/api/login", "public", "anonymous-intended-behavior")]
    public async Task Login_WithValidCredentials_ReturnsTokenAndUserInfo()
    {
        await SeedUserAsync(name: "loginuser", email: "login@example.com", password: "mypassword", elo: 1500);

        var request = new
        {
            name = "loginuser",
            password = "mypassword"
        };

        var response = await Client.PostAsJsonAsync("/api/login", request);

        response.StatusCode.Should().Be(HttpStatusCode.OK);

        var body = await response.Content.ReadFromJsonAsync<LoginResponse>();
        body.Should().NotBeNull();
        body!.Token.Should().NotBeNullOrWhiteSpace();
        body.User.Should().NotBeNull();
        body.User!.Name.Should().Be("loginuser");
        body.User.Email.Should().Be("login@example.com");
        body.User.Elo.Should().Be(1500);
        body.User.ProfileRank.Should().Be("Junior 1");
        body.User.IsVisibleInRanking.Should().BeTrue();
    }

    [Test]
    public async Task Login_WithInvalidPassword_ReturnsUnauthorized()
    {
        await SeedUserAsync(name: "loginuser", email: "login@example.com", password: "correctpassword");

        var request = new
        {
            name = "loginuser",
            password = "wrongpassword"
        };

        var response = await Client.PostAsJsonAsync("/api/login", request);

        response.StatusCode.Should().Be(HttpStatusCode.Unauthorized);

        var body = await response.Content.ReadFromJsonAsync<MessageResponse>();
        body.Should().NotBeNull();
        body!.Message.Should().Be("Unauthorized");
    }

    [Test]
    public async Task Login_WithNonExistentUser_ReturnsUnauthorized()
    {
        var request = new
        {
            name = "nonexistent",
            password = "somepassword"
        };

        var response = await Client.PostAsJsonAsync("/api/login", request);

        response.StatusCode.Should().Be(HttpStatusCode.Unauthorized);

        var body = await response.Content.ReadFromJsonAsync<MessageResponse>();
        body.Should().NotBeNull();
        body!.Message.Should().Be("Unauthorized");
    }

    [Test]
    public async Task Login_WithEmptyCredentials_ReturnsBadRequest()
    {
        var request = new
        {
            name = "",
            password = ""
        };

        var response = await Client.PostAsJsonAsync("/api/login", request);

        response.StatusCode.Should().Be(HttpStatusCode.BadRequest);
    }

    [Test]
    [LgymApi.IntegrationTests.Authorization.AuthorizationEvidence("GET", "/api/checkToken", "own", "owner-allow")]
    [LgymApi.IntegrationTests.Authorization.AuthorizationEvidence("GET", "/api/checkToken", "own", "no-client-subject")]
    public async Task CheckToken_WithValidToken_ReturnsUserInfo()
    {
        var user = await SeedUserAsync(name: "tokenuser", email: "token@example.com", elo: 2000);
        SetAuthorizationHeader(user.Id);

        var response = await Client.GetAsync("/api/checkToken");

        response.StatusCode.Should().Be(HttpStatusCode.OK);

        var body = await response.Content.ReadFromJsonAsync<UserInfoResponse>();
        body.Should().NotBeNull();
        body!.Name.Should().Be("tokenuser");
        body.Email.Should().Be("token@example.com");
        body.Elo.Should().Be(2000);
    }

    [Test]
    public async Task CheckToken_WithoutToken_ReturnsUnauthorized()
    {
        ClearAuthorizationHeader();

        var response = await Client.GetAsync("/api/checkToken");

        response.StatusCode.Should().Be(HttpStatusCode.Unauthorized);

        var body = await response.Content.ReadFromJsonAsync<MiddlewareErrorResponse>();
        body.Should().NotBeNull();
        body!.Message.Should().Be("Invalid JWT token.");
    }

    [Test]
    public async Task CheckToken_WithInvalidToken_ReturnsUnauthorized()
    {
        Client.DefaultRequestHeaders.Authorization = new System.Net.Http.Headers.AuthenticationHeaderValue("Bearer", "invalid.jwt.token");

        var response = await Client.GetAsync("/api/checkToken");

        response.StatusCode.Should().Be(HttpStatusCode.Unauthorized);

        var body = await response.Content.ReadFromJsonAsync<MiddlewareErrorResponse>();
        body.Should().NotBeNull();
        body!.Message.Should().Be("Invalid JWT token.");
    }

    [Test]
    public async Task CheckToken_WithDeletedUser_ReturnsUnauthorized()
    {
        var user = await SeedUserAsync(name: "deleteduser", email: "deleted@example.com", isDeleted: true);
        SetAuthorizationHeader(user.Id);

        var response = await Client.GetAsync("/api/checkToken");

        response.StatusCode.Should().Be(HttpStatusCode.Unauthorized);

        var body = await response.Content.ReadFromJsonAsync<MiddlewareErrorResponse>();
        body.Should().NotBeNull();
        body!.Message.Should().Be("Unauthorized");
    }

    [Test]
    [LgymApi.IntegrationTests.Authorization.AuthorizationEvidence("POST", "/api/logout", "own", "owner-allow")]
    [LgymApi.IntegrationTests.Authorization.AuthorizationEvidence("POST", "/api/logout", "own", "no-client-subject")]
    public async Task Logout_InvalidatesCurrentJwt()
    {
        await SeedUserAsync(name: "logoutuser", email: "logout@example.com", password: "logoutpass");

        var loginResponse = await Client.PostAsJsonAsync("/api/login", new
        {
            name = "logoutuser",
            password = "logoutpass"
        });
        loginResponse.StatusCode.Should().Be(HttpStatusCode.OK);
        var loginBody = await loginResponse.Content.ReadFromJsonAsync<LoginResponse>();
        loginBody.Should().NotBeNull();

        Client.DefaultRequestHeaders.Authorization = new System.Net.Http.Headers.AuthenticationHeaderValue("Bearer", loginBody!.Token);

        var logoutResponse = await Client.PostAsync("/api/logout", null);
        logoutResponse.StatusCode.Should().Be(HttpStatusCode.OK);

        var checkTokenResponse = await Client.GetAsync("/api/checkToken");
        checkTokenResponse.StatusCode.Should().Be(HttpStatusCode.Unauthorized);

        var body = await checkTokenResponse.Content.ReadFromJsonAsync<MiddlewareErrorResponse>();
        body.Should().NotBeNull();
        body!.Message.Should().Be("Unauthorized");
    }

    [Test]
    [LgymApi.IntegrationTests.Authorization.AuthorizationEvidence("GET", "/api/deleteAccount", "own", "owner-allow")]
    [LgymApi.IntegrationTests.Authorization.AuthorizationEvidence("GET", "/api/deleteAccount", "own", "no-client-subject")]
    public async Task DeleteAccount_WithValidToken_AnonymizesUserAndReturnsDeleted()
    {
        var user = await SeedUserAsync(name: "todelete", email: "todelete@example.com");
        SetAuthorizationHeader(user.Id);

        var response = await Client.GetAsync("/api/deleteAccount");

        response.StatusCode.Should().Be(HttpStatusCode.OK);

        var body = await response.Content.ReadFromJsonAsync<MessageResponse>();
        body.Should().NotBeNull();
        body!.Message.Should().Be("Deleted.");

        using var scope = Factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var updatedUser = await db.Users
            .IgnoreQueryFilters()
            .FirstOrDefaultAsync(u => u.Id == user.Id);

        updatedUser.Should().NotBeNull();
        updatedUser!.IsDeleted.Should().BeTrue();
        updatedUser.Name.Should().StartWith("anonymized_user_");
        updatedUser.Email.Value.Should().StartWith("anonymized_");
    }

    [Test]
    [LgymApi.IntegrationTests.Authorization.AuthorizationEvidence("POST", "/api/updateTimeZone", "own", "owner-allow")]
    public async Task UpdateTimeZone_WithAuthenticatedUser_ReturnsOk()
    {
        var user = await SeedUserAsync(name: "timezone-user", email: "timezone-user@example.com");
        SetAuthorizationHeader(user.Id);

        var response = await Client.PostAsJsonAsync("/api/updateTimeZone", new { preferredTimeZone = "UTC" });

        response.StatusCode.Should().Be(HttpStatusCode.OK);
    }

    private sealed class MessageResponse
    {
        [JsonPropertyName("msg")]
        public string Message { get; set; } = string.Empty;
    }

    /// <summary>
    /// Used for middleware error responses which use { "msg": "..." } format
    /// </summary>
    private sealed class MiddlewareErrorResponse
    {
        [JsonPropertyName("msg")]
        public string Message { get; set; } = string.Empty;
    }

    private sealed class LoginResponse
    {
        [JsonPropertyName("token")]
        public string Token { get; set; } = string.Empty;

        [JsonPropertyName("req")]
        public UserInfoResponse? User { get; set; }
    }

    private sealed class UserInfoResponse
    {
        [JsonPropertyName("name")]
        public string Name { get; set; } = string.Empty;

        [JsonPropertyName("email")]
        public string Email { get; set; } = string.Empty;

        [JsonPropertyName("elo")]
        public int Elo { get; set; }

        [JsonPropertyName("profileRank")]
        public string ProfileRank { get; set; } = string.Empty;

        [JsonPropertyName("isVisibleInRanking")]
        public bool IsVisibleInRanking { get; set; }
    }
}
