using System.Net;
using System.Net.Http.Json;
using System.Text.Json.Serialization;
using FluentAssertions;
using LgymApi.Api.Features.User.Contracts;
using LgymApi.Domain.Entities;
using LgymApi.Domain.ValueObjects;

namespace LgymApi.IntegrationTests;

[TestFixture]
public sealed class RankingTests : IntegrationTestBase
{
    [Test]
    public async Task GetUsersRanking_WithNoUsers_ReturnsNotFound()
    {
        var user = await SeedUserAsync(name: "authuser", email: "auth@example.com", isTester: true);
        SetAuthorizationHeader(user.Id);

        var response = await Client.GetAsync("/api/getUsersRanking");

        response.StatusCode.Should().Be(HttpStatusCode.NotFound);

        var body = await response.Content.ReadFromJsonAsync<MessageResponse>();
        body.Should().NotBeNull();
        body!.Message.Should().Be("Not found.");
    }

    [Test]
    [LgymApi.IntegrationTests.Authorization.AuthorizationEvidence("GET", "/api/getUsersRanking", "authenticated-global", "ordinary-authenticated-allow")]
    public async Task GetUsersRanking_WithVisibleUsers_ReturnsRankedUsers()
    {
        var user1 = await SeedUserAsync(name: "highelo", email: "high@example.com", elo: 2000);
        var user2 = await SeedUserAsync(name: "lowelo", email: "low@example.com", elo: 1200);
        SetAuthorizationHeader(user1.Id);

        var response = await Client.GetAsync("/api/getUsersRanking");

        response.StatusCode.Should().Be(HttpStatusCode.OK);

        var body = await response.Content.ReadFromJsonAsync<List<UserRankingResponse>>();
        body.Should().NotBeNull();
        body.Should().HaveCount(2);
        body![0].Name.Should().Be("highelo");
        body[0].Elo.Should().Be(2000);
        body[1].Name.Should().Be("lowelo");
        body[1].Elo.Should().Be(1200);
    }

    [Test]
    public async Task GetUsersRanking_ExcludesHiddenUsers()
    {
        var visibleUser = await SeedUserAsync(name: "visible", email: "visible@example.com", elo: 1500, isVisibleInRanking: true);
        await SeedUserAsync(name: "hidden", email: "hidden@example.com", elo: 2000, isVisibleInRanking: false);
        SetAuthorizationHeader(visibleUser.Id);

        var response = await Client.GetAsync("/api/getUsersRanking");

        response.StatusCode.Should().Be(HttpStatusCode.OK);

        var body = await response.Content.ReadFromJsonAsync<List<UserRankingResponse>>();
        body.Should().NotBeNull();
        body.Should().HaveCount(1);
        body![0].Name.Should().Be("visible");
    }

    [Test]
    public async Task GetUsersRanking_ExcludesTesters()
    {
        var normalUser = await SeedUserAsync(name: "normal", email: "normal@example.com", elo: 1500);
        await SeedUserAsync(name: "tester", email: "tester@example.com", elo: 3000, isTester: true);
        SetAuthorizationHeader(normalUser.Id);

        var response = await Client.GetAsync("/api/getUsersRanking");

        response.StatusCode.Should().Be(HttpStatusCode.OK);

        var body = await response.Content.ReadFromJsonAsync<List<UserRankingResponse>>();
        body.Should().NotBeNull();
        body.Should().HaveCount(1);
        body![0].Name.Should().Be("normal");
    }

    [Test]
    public async Task GetUsersRanking_ExcludesDeletedUsers()
    {
        var activeUser = await SeedUserAsync(name: "active", email: "active@example.com", elo: 1500);
        await SeedUserAsync(name: "deleted", email: "deleted@example.com", elo: 2500, isDeleted: true);
        SetAuthorizationHeader(activeUser.Id);

        var response = await Client.GetAsync("/api/getUsersRanking");

        response.StatusCode.Should().Be(HttpStatusCode.OK);

        var body = await response.Content.ReadFromJsonAsync<List<UserRankingResponse>>();
        body.Should().NotBeNull();
        body.Should().HaveCount(1);
        body![0].Name.Should().Be("active");
    }

    [Test]
    [LgymApi.IntegrationTests.Authorization.AuthorizationEvidence("POST", "/api/changeVisibilityInRanking", "own", "owner-allow")]
    public async Task ChangeVisibilityInRanking_SetsVisibilityToFalse()
    {
        var user = await SeedUserAsync(name: "visibilityuser", email: "vis@example.com", isVisibleInRanking: true);
        SetAuthorizationHeader(user.Id);

        var request = new ChangeVisibilityInRankingRequest { IsVisibleInRanking = false };
        var response = await Client.PostAsJsonAsync("/api/changeVisibilityInRanking", request);

        response.StatusCode.Should().Be(HttpStatusCode.OK);

        var body = await response.Content.ReadFromJsonAsync<MessageResponse>();
        body.Should().NotBeNull();
        body!.Message.Should().Be("Updated");

        var rankingResponse = await Client.GetAsync("/api/getUsersRanking");
        rankingResponse.StatusCode.Should().Be(HttpStatusCode.NotFound);
    }

    [Test]
    public async Task ChangeVisibilityInRanking_SetsVisibilityToTrue()
    {
        var user = await SeedUserAsync(name: "hiddenuser", email: "hid@example.com", isVisibleInRanking: false);
        SetAuthorizationHeader(user.Id);

        var request = new ChangeVisibilityInRankingRequest { IsVisibleInRanking = true };
        var response = await Client.PostAsJsonAsync("/api/changeVisibilityInRanking", request);

        response.StatusCode.Should().Be(HttpStatusCode.OK);

        var body = await response.Content.ReadFromJsonAsync<MessageResponse>();
        body.Should().NotBeNull();
        body!.Message.Should().Be("Updated");

        var rankingResponse = await Client.GetAsync("/api/getUsersRanking");
        rankingResponse.StatusCode.Should().Be(HttpStatusCode.OK);

        var rankings = await rankingResponse.Content.ReadFromJsonAsync<List<UserRankingResponse>>();
        rankings.Should().ContainSingle(r => r.Name == "hiddenuser");
    }

    [Test]
    public async Task ChangeVisibilityInRanking_WithoutRequiredField_ReturnsBadRequest()
    {
        var user = await SeedUserAsync(name: "badrequest", email: "bad@example.com");
        SetAuthorizationHeader(user.Id);

        var request = new ChangeVisibilityInRankingRequest { IsVisibleInRanking = null };
        var response = await Client.PostAsJsonAsync("/api/changeVisibilityInRanking", request);

        response.StatusCode.Should().Be(HttpStatusCode.BadRequest);
    }

    [Test]
    [LgymApi.IntegrationTests.Authorization.AuthorizationEvidence("GET", "/api/userInfo/{id}/getUserEloPoints", "authenticated-global", "ordinary-authenticated-allow")]
    public async Task GetUserEloPoints_WithValidUser_ReturnsElo()
    {
        var user = await SeedUserAsync(name: "elouser", email: "elo@example.com", elo: 1750);
        SetAuthorizationHeader(user.Id);

        var response = await Client.GetAsync($"/api/userInfo/{user.Id}/getUserEloPoints");

        response.StatusCode.Should().Be(HttpStatusCode.OK);

        var body = await response.Content.ReadFromJsonAsync<EloResponse>();
        body.Should().NotBeNull();
        body!.Elo.Should().Be(1750);
    }

    [Test]
    public async Task GetUserEloPoints_WithInvalidUserId_ReturnsNotFound()
    {
        var user = await SeedUserAsync(name: "authuser", email: "auth@example.com");
        SetAuthorizationHeader(user.Id);

        var response = await Client.GetAsync($"/api/userInfo/{Id<User>.New()}/getUserEloPoints");

        response.StatusCode.Should().Be(HttpStatusCode.NotFound);

        var body = await response.Content.ReadFromJsonAsync<MessageResponse>();
        body.Should().NotBeNull();
        body!.Message.Should().Be("Not found.");
    }

    [Test]
    public async Task GetUserEloPoints_WithInvalidGuidFormat_ReturnsBadRequest()
    {
        var user = await SeedUserAsync(name: "authuser", email: "auth@example.com");
        SetAuthorizationHeader(user.Id);

        var response = await Client.GetAsync("/api/userInfo/invalid-guid/getUserEloPoints");

        response.StatusCode.Should().Be(HttpStatusCode.BadRequest);

        var body = await response.Content.ReadFromJsonAsync<MessageResponse>();
        body.Should().NotBeNull();
        body!.Message.Should().Be("Not found.");
    }

    private sealed class MessageResponse
    {
        [JsonPropertyName("msg")]
        public string Message { get; set; } = string.Empty;
    }

    private sealed class UserRankingResponse
    {
        [JsonPropertyName("name")]
        public string Name { get; set; } = string.Empty;

        [JsonPropertyName("avatar")]
        public string? Avatar { get; set; }

        [JsonPropertyName("elo")]
        public int Elo { get; set; }

        [JsonPropertyName("profileRank")]
        public string ProfileRank { get; set; } = string.Empty;
    }

    private sealed class EloResponse
    {
        [JsonPropertyName("elo")]
        public int Elo { get; set; }
    }
}
