using System.Net;
using System.Net.Http.Json;
using System.Text.Json.Serialization;
using FluentAssertions;
using LgymApi.Domain.Enums;

namespace LgymApi.IntegrationTests;

[TestFixture]
public sealed class EnumTests : IntegrationTestBase
{
    [Test]
    public async Task GetEnumLookup_DoesNotReturnHiddenUnknownValue()
    {
        var (userId, _) = await RegisterUserViaEndpointAsync(
            name: "enumuser1",
            email: "enum1@example.com",
            password: "password123");
        SetAuthorizationHeader(userId);

        var response = await Client.GetAsync("/api/enums/BodyParts");

        response.StatusCode.Should().Be(HttpStatusCode.OK);

        var body = await response.Content.ReadFromJsonAsync<EnumLookupResponse>();
        body.Should().NotBeNull();
        body!.Values.Select(value => value.Id).Should().Equal(
            System.Enum.GetNames<BodyParts>().Where(name => name != "Unknown"));
        body.Values.Should().NotContain(value => value.Id == "Unknown" || value.Name == "Unknown");
    }

    [TestCase("en", "enumuser3en", "enum3-en@example.com", "Pull-up weighted")]
    [TestCase("pl", "enumuser3pl", "enum3-pl@example.com", "Podciąganie: im mniejszy ciężar, tym lepiej")]
    public async Task GetEnumLookup_Returns_Exact_PullupWeighted_Wire_Values_For_Culture(
        string culture,
        string userName,
        string email,
        string expectedLabel)
    {
        var (userId, _) = await RegisterUserViaEndpointAsync(
            name: userName,
            email: email,
            password: "password123");
        SetAuthorizationHeader(userId);

        using var request = new HttpRequestMessage(HttpMethod.Get, "/api/enums/ExerciseEloFormula");
        request.Headers.AcceptLanguage.ParseAdd(culture);
        var response = await Client.SendAsync(request);

        response.StatusCode.Should().Be(HttpStatusCode.OK);

        var body = await response.Content.ReadFromJsonAsync<EnumLookupResponse>();
        body.Should().NotBeNull();
        var pullupWeighted = body!.Values.Single(value => value.Id == "PullupWeighted");
        pullupWeighted.Id.Should().Be("PullupWeighted");
        pullupWeighted.Name.Should().Be(expectedLabel);
        pullupWeighted.DisplayName.Should().Be(expectedLabel);
    }

    [Test]
    public async Task GetEnumLookup_Matches_Enum_Type_Case_Insensitively()
    {
        var (userId, _) = await RegisterUserViaEndpointAsync(
            name: "enumusercase",
            email: "enum-case@example.com",
            password: "password123");
        SetAuthorizationHeader(userId);

        var response = await Client.GetAsync("/api/enums/eXeRcIsEeLoFoRmUlA");

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        var body = await response.Content.ReadFromJsonAsync<EnumLookupResponse>();
        body.Should().NotBeNull();
        body!.EnumType.Should().Be(nameof(ExerciseEloFormula));
        body.Values.Select(value => value.Id).Should().Contain("PullupWeighted");
    }

    [Test]
    public async Task GetAllEnumLookups_DoesNotReturnHiddenUnknownValues()
    {
        var (userId, _) = await RegisterUserViaEndpointAsync(
            name: "enumuser2",
            email: "enum2@example.com",
            password: "password123");
        SetAuthorizationHeader(userId);

        var response = await Client.GetAsync("/api/enums/all");

        response.StatusCode.Should().Be(HttpStatusCode.OK);

        var body = await response.Content.ReadFromJsonAsync<List<EnumLookupResponse>>();
        body.Should().NotBeNull();
        body!.SelectMany(lookup => lookup.Values)
            .Should().NotContain(value => value.Id == "Unknown" || value.Name == "Unknown");
    }

    private sealed class EnumLookupResponse
    {
        [JsonPropertyName("enumType")]
        public string EnumType { get; set; } = string.Empty;

        [JsonPropertyName("values")]
        public List<EnumLookupValue> Values { get; set; } = new();
    }

    private sealed class EnumLookupValue
    {
        [JsonPropertyName("id")]
        public string Id { get; set; } = string.Empty;

        [JsonPropertyName("name")]
        public string Name { get; set; } = string.Empty;

        [JsonPropertyName("displayName")]
        public string DisplayName { get; set; } = string.Empty;
    }
}
