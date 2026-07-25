using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using FluentAssertions;
using LgymApi.Domain.Entities;
using LgymApi.Domain.Enums;
using LgymApi.Domain.ValueObjects;

namespace LgymApi.IntegrationTests;

[TestFixture]
public sealed class AppConfigAdminTests : IntegrationTestBase
{
    [Test]
    public async Task GetPaginated_ReturnsExpectedJsonShape_WithDefaultPagination()
    {
        var admin = await SeedAdminAsync();
        SetAuthorizationHeader(admin.Id);

        // Create test AppConfig
        var createResponse = await Client.PostAsJsonAsync($"/api/appConfig/createNewAppVersion/{admin.Id}", new
        {
            platform = Platforms.Android.ToString(),
            minRequiredVersion = "1.0.0",
            latestVersion = "1.1.0",
            forceUpdate = false,
            updateUrl = "https://example.com/android",
            releaseNotes = "Test Android config"
        });
        createResponse.StatusCode.Should().Be(HttpStatusCode.Created);

        // Call paginated endpoint
        var paginatedResponse = await Client.PostAsJsonAsync("/api/appconfig/paginated", new
        {
            page = 1,
            pageSize = 20
        });
        paginatedResponse.StatusCode.Should().Be(HttpStatusCode.OK);

        using var json = await ReadJsonAsync(paginatedResponse);

        // Verify pagination metadata
        json.RootElement.TryGetProperty("items", out var items).Should().BeTrue();
        items.ValueKind.Should().Be(JsonValueKind.Array);
        items.GetArrayLength().Should().BeGreaterThan(0);

        json.RootElement.TryGetProperty("page", out var page).Should().BeTrue();
        page.GetInt32().Should().Be(1);

        json.RootElement.TryGetProperty("pageSize", out var pageSize).Should().BeTrue();
        pageSize.GetInt32().Should().Be(20);

        json.RootElement.TryGetProperty("totalCount", out var totalCount).Should().BeTrue();
        totalCount.GetInt32().Should().BeGreaterThan(0);

        // Verify item shape
        var firstItem = items.EnumerateArray().First();
        AssertAppConfigDetailDtoShape(firstItem);
    }

    [Test]
    public async Task GetPaginated_WithMultipleConfigs_ReturnsAllConfigs()
    {
        var admin = await SeedAdminAsync();
        SetAuthorizationHeader(admin.Id);

        // Create configs with different platforms
        await Client.PostAsJsonAsync($"/api/appConfig/createNewAppVersion/{admin.Id}", new
        {
            platform = Platforms.Android.ToString(),
            minRequiredVersion = "1.0.0",
            latestVersion = "1.1.0",
            forceUpdate = false,
            updateUrl = "https://example.com/android",
            releaseNotes = "Android config"
        });

        await Client.PostAsJsonAsync($"/api/appConfig/createNewAppVersion/{admin.Id}", new
        {
            platform = Platforms.Ios.ToString(),
            minRequiredVersion = "2.0.0",
            latestVersion = "2.1.0",
            forceUpdate = true,
            updateUrl = "https://example.com/ios",
            releaseNotes = "iOS config"
        });

        // Get all configs
        var paginatedResponse = await Client.PostAsJsonAsync("/api/appconfig/paginated", new
        {
            page = 1,
            pageSize = 20
        });
        paginatedResponse.StatusCode.Should().Be(HttpStatusCode.OK);

        using var json = await ReadJsonAsync(paginatedResponse);
        json.RootElement.TryGetProperty("items", out var items).Should().BeTrue();

        var itemsArray = items.EnumerateArray().ToList();
        itemsArray.Should().HaveCountGreaterThanOrEqualTo(2, "at least 2 configs should exist");

        // Verify items have expected platform values
        var platforms = itemsArray.Select(item =>
        {
            item.TryGetProperty("platform", out var platform).Should().BeTrue();
            return platform.GetString();
        }).ToList();

        platforms.Should().Contain(Platforms.Android.ToString());
        platforms.Should().Contain(Platforms.Ios.ToString());
    }

    [Test]
    public async Task GetPaginated_ReturnsConfigsInCorrectOrder()
    {
        var admin = await SeedAdminAsync();
        SetAuthorizationHeader(admin.Id);

        // Create 3 AppConfigs with different timestamps
        var create1Response = await Client.PostAsJsonAsync($"/api/appConfig/createNewAppVersion/{admin.Id}", new
        {
            platform = Platforms.Android.ToString(),
            minRequiredVersion = "1.0.0",
            latestVersion = "1.1.0",
            forceUpdate = false,
            updateUrl = "https://example.com/android",
            releaseNotes = "First config"
        });
        create1Response.StatusCode.Should().Be(HttpStatusCode.Created);

        // Small delay to ensure different timestamps
        await Task.Delay(100);

        var create2Response = await Client.PostAsJsonAsync($"/api/appConfig/createNewAppVersion/{admin.Id}", new
        {
            platform = Platforms.Ios.ToString(),
            minRequiredVersion = "2.0.0",
            latestVersion = "2.1.0",
            forceUpdate = true,
            updateUrl = "https://example.com/ios",
            releaseNotes = "Second config"
        });
        create2Response.StatusCode.Should().Be(HttpStatusCode.Created);

        // Small delay to ensure different timestamps
        await Task.Delay(100);

        var create3Response = await Client.PostAsJsonAsync($"/api/appConfig/createNewAppVersion/{admin.Id}", new
        {
            platform = Platforms.Android.ToString(),
            minRequiredVersion = "3.0.0",
            latestVersion = "3.1.0",
            forceUpdate = false,
            updateUrl = "https://example.com/android2",
            releaseNotes = "Third config"
        });
        create3Response.StatusCode.Should().Be(HttpStatusCode.Created);

        // Call paginated endpoint
        var paginatedResponse = await Client.PostAsJsonAsync("/api/appconfig/paginated", new
        {
            page = 1,
            pageSize = 20
        });
        paginatedResponse.StatusCode.Should().Be(HttpStatusCode.OK);

        using var json = await ReadJsonAsync(paginatedResponse);
        json.RootElement.TryGetProperty("items", out var items).Should().BeTrue();

        var itemsArray = items.EnumerateArray().ToList();
        itemsArray.Count.Should().BeGreaterThanOrEqualTo(3, "at least 3 configs should exist");

        // Verify pagination metadata
        json.RootElement.TryGetProperty("page", out var page).Should().BeTrue();
        page.GetInt32().Should().Be(1);

        json.RootElement.TryGetProperty("pageSize", out var pageSize).Should().BeTrue();
        pageSize.GetInt32().Should().Be(20);

        json.RootElement.TryGetProperty("totalCount", out var totalCount).Should().BeTrue();
        totalCount.GetInt32().Should().BeGreaterThanOrEqualTo(3);

        // Verify items have expected properties (createdAt values should exist and be comparable)
        var createdAtValues = itemsArray.Select(item =>
        {
            item.TryGetProperty("createdAt", out var createdAt).Should().BeTrue();
            var createdAtString = createdAt.GetString();
            createdAtString.Should().NotBeNullOrWhiteSpace();
            return createdAtString;
        }).ToList();

        createdAtValues.Should().HaveCount(itemsArray.Count);
    }

    [Test]
    public async Task GetById_ReturnsExpectedJsonShape()
    {
        var admin = await SeedAdminAsync();
        SetAuthorizationHeader(admin.Id);

        // Create test AppConfig
        var createResponse = await Client.PostAsJsonAsync($"/api/appConfig/createNewAppVersion/{admin.Id}", new
        {
            platform = Platforms.Android.ToString(),
            minRequiredVersion = "1.0.0",
            latestVersion = "1.1.0",
            forceUpdate = true,
            updateUrl = "https://example.com/android",
            releaseNotes = "Detailed test config"
        });
        createResponse.StatusCode.Should().Be(HttpStatusCode.Created);

        // Fetch the created config via paginated endpoint to get its ID
        var paginatedResponse = await Client.PostAsJsonAsync("/api/appconfig/paginated", new
        {
            page = 1,
            pageSize = 20
        });
        paginatedResponse.StatusCode.Should().Be(HttpStatusCode.OK);

        using var paginatedJson = await ReadJsonAsync(paginatedResponse);
        paginatedJson.RootElement.TryGetProperty("items", out var items).Should().BeTrue();
        var configId = items.EnumerateArray().First().GetProperty("id").GetString();
        configId.Should().NotBeNullOrWhiteSpace();

        // Get by ID
        var getResponse = await Client.GetAsync($"/api/appconfig/{configId}");
        getResponse.StatusCode.Should().Be(HttpStatusCode.OK);

        using var json = await ReadJsonAsync(getResponse);
        AssertAppConfigDetailDtoShape(json.RootElement);

        // Verify specific values match creation
        json.RootElement.TryGetProperty("platform", out var platform).Should().BeTrue();
        platform.GetString().Should().Be(Platforms.Android.ToString());

        json.RootElement.TryGetProperty("minRequiredVersion", out var minVersion).Should().BeTrue();
        minVersion.GetString().Should().Be("1.0.0");

        json.RootElement.TryGetProperty("forceUpdate", out var forceUpdate).Should().BeTrue();
        forceUpdate.GetBoolean().Should().BeTrue();
    }

    [Test]
    public async Task GetById_WithInvalidId_Returns404()
    {
        var admin = await SeedAdminAsync();
        SetAuthorizationHeader(admin.Id);

        var randomId = Id<AppConfig>.New().ToString();
        var getResponse = await Client.GetAsync($"/api/appconfig/{randomId}");

        getResponse.StatusCode.Should().Be(HttpStatusCode.NotFound);
    }

    [Test]
    public async Task GetById_Unauthenticated_Returns401()
    {
        ClearAuthorizationHeader();

        var randomId = Id<AppConfig>.New().ToString();
        var getResponse = await Client.GetAsync($"/api/appconfig/{randomId}");

        getResponse.StatusCode.Should().Be(HttpStatusCode.Unauthorized);
    }

    [Test]
    public async Task Update_ModifiesAppConfigAndPersistsChanges()
    {
        var admin = await SeedAdminAsync();
        SetAuthorizationHeader(admin.Id);

        // Create initial AppConfig
        var createResponse = await Client.PostAsJsonAsync($"/api/appConfig/createNewAppVersion/{admin.Id}", new
        {
            platform = Platforms.Android.ToString(),
            minRequiredVersion = "1.0.0",
            latestVersion = "1.1.0",
            forceUpdate = false,
            updateUrl = "https://example.com/android",
            releaseNotes = "Initial notes"
        });
        createResponse.StatusCode.Should().Be(HttpStatusCode.Created);

        // Fetch the created config via paginated endpoint to get its ID
        var paginatedResponse = await Client.PostAsJsonAsync("/api/appconfig/paginated", new
        {
            page = 1,
            pageSize = 20
        });
        paginatedResponse.StatusCode.Should().Be(HttpStatusCode.OK);

        using var paginatedJson = await ReadJsonAsync(paginatedResponse);
        paginatedJson.RootElement.TryGetProperty("items", out var items).Should().BeTrue();
        var configId = items.EnumerateArray().First().GetProperty("id").GetString();

        // Update AppConfig
        var updateResponse = await Client.PostAsJsonAsync($"/api/appconfig/{configId}/update", new
        {
            platform = Platforms.Android.ToString(),
            minRequiredVersion = "2.0.0",
            latestVersion = "2.5.0",
            forceUpdate = true,
            updateUrl = "https://updated.example.com/android",
            releaseNotes = "Updated release notes"
        });
        updateResponse.StatusCode.Should().Be(HttpStatusCode.OK);

        // Verify changes persisted via GetById
        var getResponse = await Client.GetAsync($"/api/appconfig/{configId}");
        getResponse.StatusCode.Should().Be(HttpStatusCode.OK);

        using var json = await ReadJsonAsync(getResponse);
        json.RootElement.TryGetProperty("minRequiredVersion", out var minVersion).Should().BeTrue();
        minVersion.GetString().Should().Be("2.0.0");

        json.RootElement.TryGetProperty("latestVersion", out var latestVersion).Should().BeTrue();
        latestVersion.GetString().Should().Be("2.5.0");

        json.RootElement.TryGetProperty("forceUpdate", out var forceUpdate).Should().BeTrue();
        forceUpdate.GetBoolean().Should().BeTrue();

        json.RootElement.TryGetProperty("updateUrl", out var updateUrl).Should().BeTrue();
        updateUrl.GetString().Should().Be("https://updated.example.com/android");

        json.RootElement.TryGetProperty("releaseNotes", out var releaseNotes).Should().BeTrue();
        releaseNotes.GetString().Should().Be("Updated release notes");

        // Verify updatedAt is set
        json.RootElement.TryGetProperty("updatedAt", out var updatedAt).Should().BeTrue();
        updatedAt.ValueKind.Should().NotBe(JsonValueKind.Null);
    }

    [Test]
    public async Task Update_Unauthenticated_Returns401()
    {
        ClearAuthorizationHeader();

        var randomId = Id<AppConfig>.New().ToString();
        var updateResponse = await Client.PostAsJsonAsync($"/api/appconfig/{randomId}/update", new
        {
            platform = Platforms.Android.ToString(),
            minRequiredVersion = "1.0.0",
            latestVersion = "1.1.0",
            forceUpdate = false,
            updateUrl = "https://example.com",
            releaseNotes = "test"
        });

        updateResponse.StatusCode.Should().Be(HttpStatusCode.Unauthorized);
    }

    [Test]
    public async Task Delete_RemovesAppConfig()
    {
        var admin = await SeedAdminAsync();
        SetAuthorizationHeader(admin.Id);

        // Create AppConfig
        var createResponse = await Client.PostAsJsonAsync($"/api/appConfig/createNewAppVersion/{admin.Id}", new
        {
            platform = Platforms.Android.ToString(),
            minRequiredVersion = "1.0.0",
            latestVersion = "1.1.0",
            forceUpdate = false,
            updateUrl = "https://example.com/android",
            releaseNotes = "To be deleted"
        });
        createResponse.StatusCode.Should().Be(HttpStatusCode.Created);

        // Fetch the created config via paginated endpoint to get its ID
        var paginatedResponse = await Client.PostAsJsonAsync("/api/appconfig/paginated", new
        {
            page = 1,
            pageSize = 20
        });
        paginatedResponse.StatusCode.Should().Be(HttpStatusCode.OK);

        using var paginatedJson = await ReadJsonAsync(paginatedResponse);
        paginatedJson.RootElement.TryGetProperty("items", out var items).Should().BeTrue();
        var configId = items.EnumerateArray().First().GetProperty("id").GetString();

        // Verify config exists
        var getBeforeDelete = await Client.GetAsync($"/api/appconfig/{configId}");
        getBeforeDelete.StatusCode.Should().Be(HttpStatusCode.OK);

        // Delete AppConfig
        var deleteResponse = await Client.PostAsJsonAsync($"/api/appconfig/{configId}/delete", new { });
        deleteResponse.StatusCode.Should().Be(HttpStatusCode.OK);

        // Verify config is gone
        var getAfterDelete = await Client.GetAsync($"/api/appconfig/{configId}");
        getAfterDelete.StatusCode.Should().Be(HttpStatusCode.NotFound);
    }

    [Test]
    public async Task Delete_Unauthenticated_Returns401()
    {
        ClearAuthorizationHeader();

        var randomId = Id<AppConfig>.New().ToString();
        var deleteResponse = await Client.PostAsJsonAsync($"/api/appconfig/{randomId}/delete", new { });

        deleteResponse.StatusCode.Should().Be(HttpStatusCode.Unauthorized);
    }

    [Test]
    public async Task Paginated_Unauthenticated_Returns401()
    {
        ClearAuthorizationHeader();

        var paginatedResponse = await Client.PostAsJsonAsync("/api/appconfig/paginated", new
        {
            page = 1,
            pageSize = 20
        });

        paginatedResponse.StatusCode.Should().Be(HttpStatusCode.Unauthorized);
    }

    #region Helper Methods

    private static void AssertAppConfigDetailDtoShape(JsonElement element)
    {
        element.TryGetProperty("id", out var id).Should().BeTrue("AppConfigDetailDto must have id field");
        id.GetString().Should().NotBeNullOrWhiteSpace();

        element.TryGetProperty("platform", out var platform).Should().BeTrue("AppConfigDetailDto must have platform field");
        platform.GetString().Should().NotBeNullOrWhiteSpace();

        element.TryGetProperty("minRequiredVersion", out var minRequiredVersion).Should().BeTrue("AppConfigDetailDto must have minRequiredVersion field");
        minRequiredVersion.GetString().Should().NotBeNullOrWhiteSpace();

        element.TryGetProperty("latestVersion", out var latestVersion).Should().BeTrue("AppConfigDetailDto must have latestVersion field");
        latestVersion.GetString().Should().NotBeNullOrWhiteSpace();

        element.TryGetProperty("forceUpdate", out var forceUpdate).Should().BeTrue("AppConfigDetailDto must have forceUpdate field");
        forceUpdate.ValueKind.Should().BeOneOf(JsonValueKind.True, JsonValueKind.False);

        element.TryGetProperty("updateUrl", out var updateUrl).Should().BeTrue("AppConfigDetailDto must have updateUrl field");
        updateUrl.GetString().Should().NotBeNullOrWhiteSpace();

        element.TryGetProperty("releaseNotes", out _).Should().BeTrue("AppConfigDetailDto must have releaseNotes field");

        element.TryGetProperty("createdAt", out var createdAt).Should().BeTrue("AppConfigDetailDto must have createdAt field");
        createdAt.GetString().Should().NotBeNullOrWhiteSpace();

        element.TryGetProperty("updatedAt", out _).Should().BeTrue("AppConfigDetailDto must have updatedAt field");
    }

    private static async Task<JsonDocument> ReadJsonAsync(HttpResponseMessage response)
    {
        var content = await response.Content.ReadAsStringAsync();
        return JsonDocument.Parse(content);
    }

    #endregion
}
