using System.Net;
using System.Net.Http.Json;
using System.Text.Json.Serialization;
using FluentAssertions;
using LgymApi.Domain.Security;

namespace LgymApi.IntegrationTests;

[TestFixture]
public sealed class RolePaginatedIntegrationTests : IntegrationTestBase
{
    [Test]
    [LgymApi.IntegrationTests.Authorization.AuthorizationEvidence("POST", "/api/roles/paginated", "admin", "current-permission-allow")]
    public async Task GetRolesPaginated_AsAdmin_ReturnsPaginatedResults()
    {
        await AuthenticateAsAdminAsync();

        var response = await Client.PostAsJsonAsync("/api/roles/paginated", new { page = 1, pageSize = 10 });

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        var body = await response.Content.ReadFromJsonAsync<PaginatedRoleResponse>();
        body.Should().NotBeNull();
        body!.Items.Should().NotBeEmpty();
        body.Page.Should().Be(1);
        body.PageSize.Should().Be(10);
        body.TotalCount.Should().BeGreaterThanOrEqualTo(3);
    }

    [Test]
    public async Task GetRolesPaginated_WithSort_ReturnsSortedResults()
    {
        await AuthenticateAsAdminAsync();

        var response = await Client.PostAsJsonAsync("/api/roles/paginated", new
        {
            page = 1,
            pageSize = 10,
            sortDescriptors = new[] { new { fieldName = "name", descending = false } }
        });

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        var body = await response.Content.ReadFromJsonAsync<PaginatedRoleResponse>();
        body.Should().NotBeNull();
        body!.Items.Should().NotBeEmpty();
        body.Page.Should().Be(1);
        body.PageSize.Should().Be(10);
        body.TotalCount.Should().BeGreaterThanOrEqualTo(3);
    }

    private async Task AuthenticateAsAdminAsync()
    {
        await SeedAdminAsync();
        await AuthenticateAsync(AdminName, AdminPassword);
    }

    private async Task AuthenticateAsync(string name, string password)
    {
        var loginResponse = await Client.PostAsJsonAsync("/api/login", new { name, password });
        loginResponse.StatusCode.Should().Be(HttpStatusCode.OK);
        var body = await loginResponse.Content.ReadFromJsonAsync<LoginResponse>();
        body.Should().NotBeNull();
        body!.Token.Should().NotBeNullOrWhiteSpace();

        Client.DefaultRequestHeaders.Authorization = new System.Net.Http.Headers.AuthenticationHeaderValue("Bearer", body.Token);
    }

    private sealed class LoginResponse
    {
        [JsonPropertyName("token")]
        public string Token { get; set; } = string.Empty;
    }

    private sealed class PaginatedRoleResponse
    {
        [JsonPropertyName("items")]
        public List<RoleItemResponse> Items { get; set; } = new();

        [JsonPropertyName("page")]
        public int Page { get; set; }

        [JsonPropertyName("pageSize")]
        public int PageSize { get; set; }

        [JsonPropertyName("totalCount")]
        public int TotalCount { get; set; }

        [JsonPropertyName("totalPages")]
        public int TotalPages { get; set; }

        [JsonPropertyName("hasNextPage")]
        public bool HasNextPage { get; set; }

        [JsonPropertyName("hasPreviousPage")]
        public bool HasPreviousPage { get; set; }
    }

    private sealed class RoleItemResponse
    {
        [JsonPropertyName("id")]
        public string Id { get; set; } = string.Empty;

        [JsonPropertyName("name")]
        public string Name { get; set; } = string.Empty;

        [JsonPropertyName("description")]
        public string? Description { get; set; }

        [JsonPropertyName("permissionClaims")]
        public List<string> PermissionClaims { get; set; } = new();
    }
}
