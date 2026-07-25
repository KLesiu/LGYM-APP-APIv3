using System.Net;
using System.Net.Http.Json;
using System.Text.Json.Serialization;
using FluentAssertions;
using LgymApi.Domain.ValueObjects;

namespace LgymApi.IntegrationTests;

[TestFixture]
public sealed class EloRegistryTests : IntegrationTestBase
{
    [Test]
    public async Task Register_CreatesInitialEloEntry()
    {
        var (userId, token) = await RegisterUserViaEndpointAsync(
            name: "elouser",
            email: "elo@example.com",
            password: "password123");

        Client.DefaultRequestHeaders.Authorization =
            new System.Net.Http.Headers.AuthenticationHeaderValue("Bearer", token);

        var response = await Client.GetAsync($"/api/userInfo/{userId}/getUserEloPoints");

        response.StatusCode.Should().Be(HttpStatusCode.OK);

        var body = await response.Content.ReadFromJsonAsync<EloResponse>();
        body.Should().NotBeNull();
        body!.Elo.Should().Be(1000);
    }

    [Test]
    public async Task GetEloRegistryChart_WithSingleEntry_ReturnsChart()
    {
        var (userId, token) = await RegisterUserViaEndpointAsync(
            name: "chartuser",
            email: "chart@example.com",
            password: "password123");

        Client.DefaultRequestHeaders.Authorization =
            new System.Net.Http.Headers.AuthenticationHeaderValue("Bearer", token);

        var response = await Client.GetAsync($"/api/eloRegistry/{userId}/getEloRegistryChart");

        response.StatusCode.Should().Be(HttpStatusCode.OK);

        var body = await response.Content.ReadFromJsonAsync<List<EloChartEntry>>();
        body.Should().NotBeNull();
        body.Should().HaveCountGreaterThanOrEqualTo(1);
        body![0].Value.Should().Be(1000);
        Id<LgymApi.Domain.Entities.EloRegistry>.TryParse(body[0].Id, out var parsedId).Should().BeTrue();
        parsedId.ToString().Should().Be(body[0].Id);

        var repeatResponse = await Client.GetAsync($"/api/eloRegistry/{userId}/getEloRegistryChart");
        repeatResponse.StatusCode.Should().Be(HttpStatusCode.OK);
        var repeatBody = await repeatResponse.Content.ReadFromJsonAsync<List<EloChartEntry>>();
        repeatBody.Should().NotBeNull();
        repeatBody!.Select(entry => entry.Id).Should().Equal(body.Select(entry => entry.Id));
        TestContext.Progress.WriteLine(System.Text.Json.JsonSerializer.Serialize(body));
    }

    [Test]
    public async Task GetEloRegistryChart_WithMismatchedRouteUserId_ReturnsForbidden()
    {
        var (userId, token) = await RegisterUserViaEndpointAsync(
            name: "authuser",
            email: "auth@example.com",
            password: "password123");

        Client.DefaultRequestHeaders.Authorization =
            new System.Net.Http.Headers.AuthenticationHeaderValue("Bearer", token);

        var (otherUserId, _) = await RegisterUserViaEndpointAsync(
            name: "eloother",
            email: "eloother@example.com",
            password: "password123");

        var response = await Client.GetAsync($"/api/eloRegistry/{otherUserId}/getEloRegistryChart");

        response.StatusCode.Should().Be(HttpStatusCode.Forbidden);
    }

    [Test]
    public async Task GetEloRegistryChart_WithInvalidGuidFormat_ReturnsForbidden()
    {
        var (userId, token) = await RegisterUserViaEndpointAsync(
            name: "authuser2",
            email: "auth2@example.com",
            password: "password123");

        Client.DefaultRequestHeaders.Authorization =
            new System.Net.Http.Headers.AuthenticationHeaderValue("Bearer", token);

        var response = await Client.GetAsync("/api/eloRegistry/invalid-guid/getEloRegistryChart");

        response.StatusCode.Should().Be(HttpStatusCode.Forbidden);
    }

    [Test]
    public async Task GetUserEloPoints_WithValidUser_ReturnsElo()
    {
        var (userId, token) = await RegisterUserViaEndpointAsync(
            name: "elopointsuser",
            email: "elopoints@example.com",
            password: "password123");

        Client.DefaultRequestHeaders.Authorization =
            new System.Net.Http.Headers.AuthenticationHeaderValue("Bearer", token);

        var response = await Client.GetAsync($"/api/userInfo/{userId}/getUserEloPoints");

        response.StatusCode.Should().Be(HttpStatusCode.OK);

        var body = await response.Content.ReadFromJsonAsync<EloResponse>();
        body.Should().NotBeNull();
        body!.Elo.Should().Be(1000);
    }

    [Test]
    public async Task GetUserEloPoints_WithInvalidUserId_ReturnsNotFound()
    {
        var (userId, token) = await RegisterUserViaEndpointAsync(
            name: "authuser3",
            email: "auth3@example.com",
            password: "password123");

        Client.DefaultRequestHeaders.Authorization =
            new System.Net.Http.Headers.AuthenticationHeaderValue("Bearer", token);

        var nonExistentId = Domain.ValueObjects.Id<object>.New();
        var response = await Client.GetAsync($"/api/userInfo/{nonExistentId}/getUserEloPoints");

        response.StatusCode.Should().Be(HttpStatusCode.NotFound);
    }

    [Test]
    public async Task Login_ReturnsCurrentElo()
    {
        await RegisterUserViaEndpointAsync(
            name: "loginuser",
            email: "login@example.com",
            password: "password123");

        var loginRequest = new { name = "loginuser", password = "password123" };
        var response = await Client.PostAsJsonAsync("/api/login", loginRequest);

        response.StatusCode.Should().Be(HttpStatusCode.OK);

        var body = await response.Content.ReadFromJsonAsync<LoginResponse>();
        body.Should().NotBeNull();
        body!.User.Should().NotBeNull();
        body.User!.Elo.Should().Be(1000);
    }

    [Test]
    public async Task CheckToken_ReturnsCurrentElo()
    {
        var (userId, token) = await RegisterUserViaEndpointAsync(
            name: "tokenuser",
            email: "token@example.com",
            password: "password123");

        Client.DefaultRequestHeaders.Authorization =
            new System.Net.Http.Headers.AuthenticationHeaderValue("Bearer", token);

        var response = await Client.GetAsync("/api/checkToken");

        response.StatusCode.Should().Be(HttpStatusCode.OK);

        var body = await response.Content.ReadFromJsonAsync<UserInfoResponse>();
        body.Should().NotBeNull();
        body!.Elo.Should().Be(1000);
    }

    private sealed class EloResponse
    {
        [JsonPropertyName("elo")]
        public int Elo { get; set; }
    }

    private sealed class EloChartEntry
    {
        [JsonPropertyName("_id")]
        public string Id { get; set; } = string.Empty;

        [JsonPropertyName("value")]
        public int Value { get; set; }

        [JsonPropertyName("date")]
        public string Date { get; set; } = string.Empty;
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

        [JsonPropertyName("elo")]
        public int Elo { get; set; }
    }
}
