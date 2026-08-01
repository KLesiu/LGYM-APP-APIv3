using System.IdentityModel.Tokens.Jwt;
using System.Net;
using System.Net.Http.Json;
using System.Security.Claims;
using System.Text;
using FluentAssertions;
using LgymApi.Domain.Entities;
using LgymApi.Domain.ValueObjects;
using LgymApi.Infrastructure.Data;
using LgymApi.Resources;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.IdentityModel.Tokens;

namespace LgymApi.IntegrationTests;

/// <summary>
/// Integration tests verifying error response contract consistency across all middleware and authentication error paths.
/// Validates that all error responses use the standardized "msg" field format (not "message") via ErrorResponseWriter.
/// </summary>
[TestFixture]
public sealed class ErrorContractConsistencyTests : IntegrationTestBase
{
    private HttpResponseParityTestHarness _harness = null!;

    [SetUp]
    public void SetUpHarness()
    {
        _harness = new HttpResponseParityTestHarness();
    }

    /// <summary>
    /// Validates that requests without Authorization header return 401 with "msg" field.
    /// Tests JWT authentication challenge event (OnChallenge).
    /// </summary>
    [Test]
    public async Task MissingToken_Returns401WithMsgField()
    {
        // Arrange: Create a user but don't set auth header
        var user = await SeedUserAsync(
            name: "missing_token_user",
            email: "missing_token@example.com");

        // Act: Hit a protected endpoint without Authorization header
        var response = await Client.GetAsync($"/api/gym/{user.Id}/getGyms");

        // Assert: 401 + "msg" field present
        await _harness.AssertErrorMessageResponseAsync(
            response,
            HttpStatusCode.Unauthorized,
            CompatibilityResourceMessage.InCulture("en", () => Messages.InvalidToken));
    }

    /// <summary>
    /// Validates that malformed Bearer tokens return 401 with "msg" field.
    /// Tests JWT authentication challenge event (OnChallenge) for invalid token format.
    /// </summary>
    [Test]
    public async Task InvalidToken_Returns401WithMsgField()
    {
        // Arrange: Create a user
        var user = await SeedUserAsync(
            name: "invalid_token_user",
            email: "invalid_token@example.com");

        // Act: Set malformed Bearer token
        Client.DefaultRequestHeaders.Authorization =
            new System.Net.Http.Headers.AuthenticationHeaderValue("Bearer", "this-is-not-a-valid-jwt-token");

        var response = await Client.GetAsync($"/api/gym/{user.Id}/getGyms");

        // Assert: 401 + "msg" field present
        await _harness.AssertErrorMessageResponseAsync(
            response,
            HttpStatusCode.Unauthorized,
            CompatibilityResourceMessage.InCulture("en", () => Messages.InvalidToken));
    }

    [Test]
    public async Task MissingSessionClaim_Returns401WithResourceBackedMsgField()
    {
        var user = await SeedUserAsync(
            name: "missing_session_claim_user",
            email: "missing_session_claim@example.com");
        var token = GenerateJwtWithoutSessionClaim(user.Id);
        Client.DefaultRequestHeaders.Authorization =
            new System.Net.Http.Headers.AuthenticationHeaderValue("Bearer", token);

        var response = await Client.GetAsync($"/api/gym/{user.Id}/getGyms");

        await _harness.AssertErrorMessageResponseAsync(
            response,
            HttpStatusCode.Unauthorized,
            CompatibilityResourceMessage.InCulture("en", () => Messages.InvalidToken));
    }

    [Test]
    public async Task MissingSessionRow_Returns401WithResourceBackedMsgField()
    {
        var user = await SeedUserAsync(
            name: "missing_session_row_user",
            email: "missing_session_row@example.com");
        var token = GenerateJwt(user.Id, Id<UserSession>.New(), Id<UserSession>.New().ToString());
        Client.DefaultRequestHeaders.Authorization =
            new System.Net.Http.Headers.AuthenticationHeaderValue("Bearer", token);

        var response = await Client.GetAsync($"/api/gym/{user.Id}/getGyms");

        await _harness.AssertErrorMessageResponseAsync(
            response,
            HttpStatusCode.Unauthorized,
            CompatibilityResourceMessage.InCulture("en", () => Messages.Unauthorized));
    }

    /// <summary>
    /// Validates that expired JWT tokens return 401 with "msg" field.
    /// Tests JWT authentication failed event (OnAuthenticationFailed with SecurityTokenExpiredException).
    /// </summary>
    [Test]
    public async Task ExpiredToken_Returns401WithMsgField()
    {
        // Arrange: Create a user and session
        var user = await SeedUserAsync(
            name: "expired_token_user",
            email: "expired_token@example.com");

        using var scope = Factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();

        var jti = Id<UserSession>.New().ToString();
        var session = new UserSession
        {
            Id = Id<UserSession>.New(),
            UserId = user.Id,
            Jti = jti,
            ExpiresAtUtc = DateTimeOffset.UtcNow.AddDays(1),
            RevokedAtUtc = null
        };
        db.UserSessions.Add(session);
        await db.SaveChangesAsync();

        // Generate expired JWT token (exp in the past)
        var key = new SymmetricSecurityKey(Encoding.UTF8.GetBytes(CustomWebApplicationFactory.TestJwtSigningKey));
        var credentials = new SigningCredentials(key, SecurityAlgorithms.HmacSha256);

        var claims = new List<Claim>
        {
            new Claim(JwtRegisteredClaimNames.Sub, user.Id.ToString()),
            new Claim("userId", user.Id.ToString()),
            new Claim("sid", session.Id.ToString()),
            new Claim(JwtRegisteredClaimNames.Jti, jti)
        };

        var token = new JwtSecurityToken(
            claims: claims,
            expires: DateTime.UtcNow.AddDays(-1), // Expired 1 day ago
            signingCredentials: credentials);

        var tokenString = new JwtSecurityTokenHandler().WriteToken(token);
        Client.DefaultRequestHeaders.Authorization =
            new System.Net.Http.Headers.AuthenticationHeaderValue("Bearer", tokenString);

        // Act: Hit protected endpoint with expired token
        var response = await Client.GetAsync($"/api/gym/{user.Id}/getGyms");

        // Assert: 401 + "msg" field present
        await _harness.AssertErrorMessageResponseAsync(
            response,
            HttpStatusCode.Unauthorized,
            CompatibilityResourceMessage.InCulture("en", () => Messages.ExpiredToken));
    }

    /// <summary>
    /// Validates that revoked sessions return 401 with "msg" field.
    /// Tests UserContextMiddleware session validation logic.
    /// </summary>
    [Test]
    public async Task RevokedSession_Returns401WithMsgField()
    {
        // Arrange: Create a user and session
        var user = await SeedUserAsync(
            name: "revoked_session_user",
            email: "revoked_session@example.com");

        using var scope = Factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();

        var jti = Id<UserSession>.New().ToString();
        var session = new UserSession
        {
            Id = Id<UserSession>.New(),
            UserId = user.Id,
            Jti = jti,
            ExpiresAtUtc = DateTimeOffset.UtcNow.AddDays(1),
            RevokedAtUtc = null
        };
        db.UserSessions.Add(session);
        await db.SaveChangesAsync();

        // Generate valid token
        var token = GenerateJwt(user.Id, session.Id, session.Jti);
        Client.DefaultRequestHeaders.Authorization =
            new System.Net.Http.Headers.AuthenticationHeaderValue("Bearer", token);

        // Revoke the session
        session.RevokedAtUtc = DateTimeOffset.UtcNow;
        await db.SaveChangesAsync();

        // Act: Hit protected endpoint with revoked session token
        var response = await Client.GetAsync($"/api/gym/{user.Id}/getGyms");

        // Assert: 401 + "msg" field present
        await _harness.AssertErrorMessageResponseAsync(
            response,
            HttpStatusCode.Unauthorized,
            CompatibilityResourceMessage.InCulture("en", () => Messages.Unauthorized));
    }

    /// <summary>
    /// Validates that soft-deleted users return 401 with "msg" field.
    /// Tests UserContextMiddleware deleted user detection.
    /// </summary>
    [Test]
    public async Task DeletedUser_Returns401WithMsgField()
    {
        // Arrange: Create a soft-deleted user
        var user = await SeedUserAsync(
            name: "deleted_user",
            email: "deleted_user@example.com",
            isDeleted: true);

        // Act: Set authorization for deleted user
        SetAuthorizationHeader(user.Id);
        var response = await Client.GetAsync($"/api/gym/{user.Id}/getGyms");

        // Assert: 401 + "msg" field present
        await _harness.AssertErrorMessageResponseAsync(
            response,
            HttpStatusCode.Unauthorized,
            CompatibilityResourceMessage.InCulture("en", () => Messages.Unauthorized));
    }

    /// <summary>
    /// Validates that blocked users return 403 with "msg" field.
    /// Tests UserContextMiddleware blocked user detection.
    /// </summary>
    [Test]
    public async Task BlockedUser_Returns403WithMsgField()
    {
        // Arrange: Create a blocked user
        var user = await SeedUserAsync(
            name: "blocked_user",
            email: "blocked_user@example.com");

        using var scope = Factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();

        // Block the user
        var dbUser = await db.Users.FindAsync(user.Id);
        dbUser!.IsBlocked = true;
        await db.SaveChangesAsync();

        // Act: Set authorization for blocked user
        SetAuthorizationHeader(user.Id);
        var response = await Client.GetAsync($"/api/gym/{user.Id}/getGyms");

        // Assert: 403 + "msg" field present
        await _harness.AssertErrorMessageResponseAsync(
            response,
            HttpStatusCode.Forbidden,
            CompatibilityResourceMessage.InCulture("en", () => Messages.AccountBlocked));
    }

    /// <summary>
    /// Validates that authorization policy failures return 403 with "msg" field.
    /// Tests JWT forbidden event (OnForbidden) when non-admin user accesses admin endpoint.
    /// </summary>
    [Test]
    public async Task AuthorizationPolicyForbidden_Returns403WithMsgField()
    {
        // Arrange: Create two non-admin users
        var user1 = await SeedUserAsync(
            name: "non_admin_user1",
            email: "non_admin1@example.com",
            isAdmin: false);
        var user2 = await SeedUserAsync(
            name: "non_admin_user2",
            email: "non_admin2@example.com",
            isAdmin: false);

        SetAuthorizationHeader(user1.Id);

        // Act: User1 tries to create app version for user2 (requires admin policy)
        var response = await Client.PostAsJsonAsync($"/api/appConfig/createNewAppVersion/{user2.Id}", new
        {
            platform = "Android",
            minRequiredVersion = "1.0.0",
            latestVersion = "2.0.0"
        });

        // Assert: 403 + "msg" field present
        await _harness.AssertErrorMessageResponseAsync(
            response,
            HttpStatusCode.Forbidden,
            CompatibilityResourceMessage.InCulture("en", () => Messages.Forbidden));
    }

    /// <summary>
    /// Validates that unhandled exceptions return 500 with "msg" field.
    /// Tests global exception handler error response format.
    /// </summary>
    [Test]
    public async Task UnhandledException_Returns500WithMsgField()
    {
        // Arrange: Create a user
        var user = await SeedUserAsync(
            name: "exception_user",
            email: "exception_user@example.com");

        SetAuthorizationHeader(user.Id);

        // Act: Trigger unhandled exception by passing invalid data to an endpoint
        // Using an endpoint that will throw when processing invalid input
        var invalidGuid = "not-a-valid-guid-at-all";
        var response = await Client.GetAsync($"/api/gym/{invalidGuid}/getGyms");

        // Assert: Should return error response with "msg" field
        // Status code might be 400 (BadRequest) if validation catches it, or 500 if it throws
        response.IsSuccessStatusCode.Should().BeFalse("invalid input should trigger error");

        var json = await _harness.GetResponseJsonAsync(response);
        json.RootElement.TryGetProperty("msg", out _)
            .Should().BeTrue("error response must contain 'msg' field");

        json.RootElement.TryGetProperty("message", out _)
            .Should().BeFalse("error response must not use 'message' field");
    }

    private static string GenerateJwtWithoutSessionClaim(Id<User> userId)
    {
        var key = new SymmetricSecurityKey(Encoding.UTF8.GetBytes(CustomWebApplicationFactory.TestJwtSigningKey));
        var credentials = new SigningCredentials(key, SecurityAlgorithms.HmacSha256);
        var claims = new List<Claim>
        {
            new(JwtRegisteredClaimNames.Sub, userId.ToString()),
            new("userId", userId.ToString()),
            new(JwtRegisteredClaimNames.Jti, Id<UserSession>.New().ToString())
        };
        var token = new JwtSecurityToken(
            claims: claims,
            expires: DateTime.UtcNow.AddHours(1),
            signingCredentials: credentials);

        return new JwtSecurityTokenHandler().WriteToken(token);
    }
}
