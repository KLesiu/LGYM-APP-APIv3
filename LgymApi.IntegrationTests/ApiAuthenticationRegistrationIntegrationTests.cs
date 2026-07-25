using System.Net;
using FluentAssertions;
using Microsoft.AspNetCore.Hosting;

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
}
