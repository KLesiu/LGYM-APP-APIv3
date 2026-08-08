using System.Net;
using System.Net.Http.Json;
using System.Text.Json.Serialization;
using FluentAssertions;
using LgymApi.Domain.Enums;
using LgymApi.Domain.ValueObjects;

namespace LgymApi.IntegrationTests;

[TestFixture]
public sealed class AppConfigTests : IntegrationTestBase
{
    [Test]
    [LgymApi.IntegrationTests.Authorization.AuthorizationEvidence("POST", "/api/appConfig/getAppVersion", "public", "anonymous-intended-behavior")]
    public async Task GetAppVersion_WithValidPlatform_ReturnsConfig()
    {
        var admin = await SeedAdminAsync();
        SetAuthorizationHeader(admin.Id);

        var createRequest = new
        {
            platform = Platforms.Android.ToString(),
            minRequiredVersion = "1.0.0",
            latestVersion = "2.0.0",
            forceUpdate = false,
            updateUrl = "https://play.google.com/store/apps/details?id=com.lgym",
            releaseNotes = "Bug fixes and improvements"
        };

        var createResponse = await Client.PostAsJsonAsync($"/api/appConfig/createNewAppVersion/{admin.Id}", createRequest);
        createResponse.StatusCode.Should().Be(HttpStatusCode.Created);

        var getRequest = new { platform = Platforms.Android.ToString() };
        var getResponse = await Client.PostAsJsonAsync("/api/appConfig/getAppVersion", getRequest);

        getResponse.StatusCode.Should().Be(HttpStatusCode.OK);

        var body = await getResponse.Content.ReadFromJsonAsync<AppConfigInfoResponse>();
        body.Should().NotBeNull();
        body!.MinRequiredVersion.Should().Be("1.0.0");
        body.LatestVersion.Should().Be("2.0.0");
        body.ForceUpdate.Should().BeFalse();
        body.UpdateUrl.Should().Be("https://play.google.com/store/apps/details?id=com.lgym");
        body.ReleaseNotes.Should().Be("Bug fixes and improvements");
    }

    private static IEnumerable<TestCaseData> GetAppVersion_InvalidPlatformCases()
    {
        yield return new TestCaseData(new { platform = "InvalidPlatform" })
            .SetName("GetAppVersion_WithInvalidPlatform_ReturnsBadRequest");
        yield return new TestCaseData(new { })
            .SetName("GetAppVersion_WithMissingPlatform_ReturnsBadRequest");
        yield return new TestCaseData(new { platform = 1 })
            .SetName("GetAppVersion_WithNumericPlatform_ReturnsBadRequest");
    }

    [TestCaseSource(nameof(GetAppVersion_InvalidPlatformCases))]
    public async Task GetAppVersion_WithInvalidPlatformData_ReturnsBadRequest(object requestBody)
    {
        var user = await SeedUserAsync(
            name: $"testuser-{TestContext.CurrentContext.Test.Name}",
            email: $"test-{Id<CustomWebApplicationFactory>.New():N}@example.com");
        SetAuthorizationHeader(user.Id);

        var response = await Client.PostAsJsonAsync("/api/appConfig/getAppVersion", requestBody);

        response.StatusCode.Should().Be(HttpStatusCode.BadRequest);
    }

    [Test]
    public async Task GetAppVersion_WithNoConfig_ReturnsNotFound()
    {
        var user = await SeedUserAsync(name: "testuser", email: "test@example.com");
        SetAuthorizationHeader(user.Id);

        var request = new { platform = Platforms.Ios.ToString() };
        var response = await Client.PostAsJsonAsync("/api/appConfig/getAppVersion", request);

        response.StatusCode.Should().Be(HttpStatusCode.NotFound);
    }

    [Test]
    [LgymApi.IntegrationTests.Authorization.AuthorizationEvidence("POST", "/api/appConfig/createNewAppVersion/{id}", "admin", "current-permission-allow")]
    public async Task CreateNewAppVersion_AsAdmin_CreatesConfig()
    {
        var admin = await SeedAdminAsync();
        SetAuthorizationHeader(admin.Id);

        var request = new
        {
            platform = Platforms.Ios.ToString(),
            minRequiredVersion = "1.5.0",
            latestVersion = "3.0.0",
            forceUpdate = true,
            updateUrl = "https://apps.apple.com/app/lgym",
            releaseNotes = "Major update"
        };

        var response = await Client.PostAsJsonAsync($"/api/appConfig/createNewAppVersion/{admin.Id}", request);

        response.StatusCode.Should().Be(HttpStatusCode.Created);

        var body = await response.Content.ReadFromJsonAsync<MessageResponse>();
        body.Should().NotBeNull();
        body!.Message.Should().Be("Created");
    }

    [Test]
    public async Task CreateNewAppVersion_AsAdminWithMismatchedRouteUserId_ReturnsForbidden()
    {
        var admin = await SeedAdminAsync();
        SetAuthorizationHeader(admin.Id);

        var otherAdmin = await SeedUserAsync(
            name: "otheradmin",
            email: "otheradmin@example.com",
            isAdmin: true);

        var request = new
        {
            platform = Platforms.Ios.ToString(),
            minRequiredVersion = "1.5.0",
            latestVersion = "3.0.0",
            forceUpdate = true,
            updateUrl = "https://apps.apple.com/app/lgym",
            releaseNotes = "Major update"
        };

        var response = await Client.PostAsJsonAsync($"/api/appConfig/createNewAppVersion/{otherAdmin.Id}", request);

        response.StatusCode.Should().Be(HttpStatusCode.Forbidden);
    }

    [Test]
    public async Task CreateNewAppVersion_AsNonAdmin_ReturnsForbidden()
    {
        var user = await SeedUserAsync(name: "normaluser", email: "normal@example.com");
        SetAuthorizationHeader(user.Id);

        var request = new
        {
            platform = Platforms.Android.ToString(),
            minRequiredVersion = "1.0.0",
            latestVersion = "2.0.0",
            forceUpdate = false
        };

        var response = await Client.PostAsJsonAsync($"/api/appConfig/createNewAppVersion/{user.Id}", request);

        response.StatusCode.Should().Be(HttpStatusCode.Forbidden);
    }

    private static IEnumerable<TestCaseData> CreateNewAppVersion_InvalidPlatformCases()
    {
        yield return new TestCaseData(new
        {
            platform = "InvalidPlatform",
            minRequiredVersion = "1.0.0",
            latestVersion = "2.0.0"
        }).SetName("CreateNewAppVersion_WithInvalidPlatform_ReturnsBadRequest");
        yield return new TestCaseData(new
        {
            minRequiredVersion = "1.0.0",
            latestVersion = "2.0.0"
        }).SetName("CreateNewAppVersion_WithMissingPlatform_ReturnsBadRequest");
        yield return new TestCaseData(new
        {
            platform = 1,
            minRequiredVersion = "1.0.0",
            latestVersion = "2.0.0"
        }).SetName("CreateNewAppVersion_WithNumericPlatform_ReturnsBadRequest");
    }

    [TestCaseSource(nameof(CreateNewAppVersion_InvalidPlatformCases))]
    public async Task CreateNewAppVersion_WithInvalidPlatformData_ReturnsBadRequest(object requestBody)
    {
        var admin = await SeedAdminAsync();
        SetAuthorizationHeader(admin.Id);

        var response = await Client.PostAsJsonAsync($"/api/appConfig/createNewAppVersion/{admin.Id}", requestBody);

        response.StatusCode.Should().Be(HttpStatusCode.BadRequest);
    }

    [Test]
    public async Task GetAppVersion_ReturnsLatestConfig_WhenMultipleExist()
    {
        var admin = await SeedAdminAsync();
        SetAuthorizationHeader(admin.Id);

        var firstRequest = new
        {
            platform = Platforms.Android.ToString(),
            minRequiredVersion = "1.0.0",
            latestVersion = "1.0.0",
            forceUpdate = false
        };
        await Client.PostAsJsonAsync($"/api/appConfig/createNewAppVersion/{admin.Id}", firstRequest);

        var secondRequest = new
        {
            platform = Platforms.Android.ToString(),
            minRequiredVersion = "1.5.0",
            latestVersion = "2.0.0",
            forceUpdate = true
        };
        await Client.PostAsJsonAsync($"/api/appConfig/createNewAppVersion/{admin.Id}", secondRequest);

        var getRequest = new { platform = Platforms.Android.ToString() };
        var getResponse = await Client.PostAsJsonAsync("/api/appConfig/getAppVersion", getRequest);

        getResponse.StatusCode.Should().Be(HttpStatusCode.OK);

        var body = await getResponse.Content.ReadFromJsonAsync<AppConfigInfoResponse>();
        body.Should().NotBeNull();
        body!.LatestVersion.Should().Be("2.0.0");
        body.MinRequiredVersion.Should().Be("1.5.0");
        body.ForceUpdate.Should().BeTrue();
    }

    private sealed class MessageResponse
    {
        [JsonPropertyName("msg")]
        public string Message { get; set; } = string.Empty;
    }

    private sealed class AppConfigInfoResponse
    {
        [JsonPropertyName("minRequiredVersion")]
        public string? MinRequiredVersion { get; set; }

        [JsonPropertyName("latestVersion")]
        public string? LatestVersion { get; set; }

        [JsonPropertyName("forceUpdate")]
        public bool ForceUpdate { get; set; }

        [JsonPropertyName("updateUrl")]
        public string? UpdateUrl { get; set; }

        [JsonPropertyName("releaseNotes")]
        public string? ReleaseNotes { get; set; }
    }
}
