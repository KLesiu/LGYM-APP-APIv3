using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json.Serialization;
using FluentAssertions;
using LgymApi.Infrastructure.Data;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace LgymApi.IntegrationTests;

[TestFixture]
public sealed class TrainerAuthTests : IntegrationTestBase
{
    private static readonly string[] ExpectedTrainerRoles = ["User", "Trainer"];

    [Test]
    [LgymApi.IntegrationTests.Authorization.AuthorizationEvidence("POST", "/api/trainer/register", "public", "anonymous-intended-behavior")]
    public async Task RegisterTrainer_WithValidData_CreatesTrainerRoleAssignment()
    {
        var request = new
        {
            name = "trainer-one",
            email = "trainer-one@example.com",
            password = "password123",
            cpassword = "password123"
        };

        SetIdempotencyKey("test-register-trainer-one");
        var response = await Client.PostAsJsonAsync("/api/trainer/register", request);
        ClearIdempotencyKey();

        response.StatusCode.Should().Be(HttpStatusCode.OK);

        using var scope = Factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();

        var user = await db.Users.FirstOrDefaultAsync(u => u.Name == "trainer-one");
        user.Should().NotBeNull();

        var roleNames = await db.UserRoles
            .Where(ur => ur.UserId == user!.Id)
            .Select(ur => ur.Role.Name)
            .ToListAsync();

        roleNames.Should().Contain(ExpectedTrainerRoles);
    }

    [Test]
    [LgymApi.IntegrationTests.Authorization.AuthorizationEvidence("POST", "/api/trainer/login", "public", "anonymous-intended-behavior")]
    [LgymApi.IntegrationTests.Authorization.AuthorizationEvidence("GET", "/api/trainer/checkToken", "own", "owner-allow")]
    [LgymApi.IntegrationTests.Authorization.AuthorizationEvidence("GET", "/api/trainer/checkToken", "own", "no-client-subject")]
    public async Task TrainerCheckToken_WithTrainerToken_ReturnsOk()
    {
        SetIdempotencyKey("test-register-trainer-auth");
        await Client.PostAsJsonAsync("/api/trainer/register", new
        {
            name = "trainer-auth",
            email = "trainer-auth@example.com",
            password = "password123",
            cpassword = "password123"
        });
        ClearIdempotencyKey();

        var loginResponse = await Client.PostAsJsonAsync("/api/trainer/login", new
        {
            name = "trainer-auth",
            password = "password123"
        });

        loginResponse.StatusCode.Should().Be(HttpStatusCode.OK);
        var loginBody = await loginResponse.Content.ReadFromJsonAsync<LoginResponse>();
        loginBody.Should().NotBeNull();
        loginBody!.Token.Should().NotBeNullOrWhiteSpace();

        Client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", loginBody.Token);

        var response = await Client.GetAsync("/api/trainer/checkToken");
        response.StatusCode.Should().Be(HttpStatusCode.OK);
    }

    [Test]
    public async Task TrainerCheckToken_WithRegularUserToken_ReturnsForbidden()
    {
        var user = await SeedUserAsync(name: "regular-user", email: "regular-user@example.com", password: "password123");
        SetAuthorizationHeader(user.Id);

        var response = await Client.GetAsync("/api/trainer/checkToken");

        response.StatusCode.Should().Be(HttpStatusCode.Forbidden);
    }

    [Test]
    public async Task TrainerLogin_WithNonTrainerUser_ReturnsUnauthorized()
    {
        await SeedUserAsync(name: "mobile-user", email: "mobile-user@example.com", password: "password123");

        var response = await Client.PostAsJsonAsync("/api/trainer/login", new
        {
            name = "mobile-user",
            password = "password123"
        });

        response.StatusCode.Should().Be(HttpStatusCode.Unauthorized);
    }

    private sealed class LoginResponse
    {
        [JsonPropertyName("token")]
        public string Token { get; set; } = string.Empty;
    }
}
