using System.Net;
using System.Net.Http.Json;
using System.Text.Json.Serialization;
using FluentAssertions;
using LgymApi.Infrastructure.Data;
using Microsoft.AspNetCore.Hosting;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace LgymApi.IntegrationTests;

[TestFixture]
public sealed class ApiAuthenticationRegistrationIntegrationTests : IntegrationTestBase
{
    [Test]
    public async Task Hub_QueryToken_AuthenticatesValidBearerToken()
    {
        var user = await SeedUserAsync(name: "hub-query-token-user", email: "hub-query-token@example.com");
        SetAuthorizationHeader(user.Id);
        var token = Client.DefaultRequestHeaders.Authorization?.Parameter;
        token.Should().NotBeNullOrWhiteSpace();
        ClearAuthorizationHeader();

        var response = await Client.PostAsync(
            $"/hubs/notifications/negotiate?negotiateVersion=1&access_token={Uri.EscapeDataString(token!)}",
            null);

        response.StatusCode.Should().Be(HttpStatusCode.OK);
    }

    [Test]
    public async Task ProtectedRoleRoute_WithRevokedSession_ReturnsUnauthorized()
    {
        // Given
        var administrator = await SeedUserAsync(
            name: "revoked-session-admin",
            email: "revoked-session-admin@example.com",
            isAdmin: true);
        SetAuthorizationHeader(administrator.Id);
        using (var scope = Factory.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
            var session = await db.UserSessions.SingleAsync(candidate => candidate.UserId == administrator.Id);
            session.RevokedAtUtc = DateTimeOffset.UnixEpoch;
            await db.SaveChangesAsync();
        }

        // When
        var response = await Client.GetAsync("/api/roles");
        var body = await response.Content.ReadFromJsonAsync<MiddlewareErrorResponse>();

        // Then
        response.StatusCode.Should().Be(HttpStatusCode.Unauthorized);
        body.Should().NotBeNull();
        body!.Message.Should().Be("Unauthorized");
    }

    [Test]
    public void ShortJwtSigningKey_PreventsTestingHostStartup()
    {
        using var baseFactory = new CustomWebApplicationFactory();
        using var factory = baseFactory.WithWebHostBuilder(builder =>
            builder.UseSetting("Jwt:SigningKey", new string('x', 31)));

        var startHost = () =>
        {
            using var client = factory.CreateClient();
        };

        startHost.Should().Throw<Exception>()
            .Which.ToString().Should().Contain("Jwt:SigningKey is not configured or is too short.");
    }

    private sealed class MiddlewareErrorResponse
    {
        [JsonPropertyName("msg")]
        public string Message { get; set; } = string.Empty;
    }
}
