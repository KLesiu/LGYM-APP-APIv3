using System.Net;
using System.Net.Http.Json;
using System.Text.Json.Serialization;
using FluentAssertions;
using LgymApi.Domain.Entities;
using LgymApi.Domain.Security;
using LgymApi.Infrastructure.Data;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace LgymApi.IntegrationTests;

[TestFixture]
public sealed class AdminUserIntegrationTests : IntegrationTestBase
{
    [Test]
    public async Task Should_ReturnForbidden_When_CalledAsNonAdmin()
    {
        var user = await SeedUserAsync(name: "regular-user", email: "regular@example.com", password: "pass1234");
        await AuthenticateAsync(user.Name, "pass1234");

        var response = await Client.PostAsJsonAsync("/api/admin/users/paginated", new { page = 1, pageSize = 10 });

        response.StatusCode.Should().Be(HttpStatusCode.Forbidden);
    }

    [Test]
    [LgymApi.IntegrationTests.Authorization.AuthorizationEvidence("POST", "/api/admin/users/paginated", "admin", "current-permission-allow")]
    public async Task Should_ReturnPaginatedResults_When_CalledAsAdmin()
    {
        await AuthenticateAsAdminAsync();
        await SeedUserAsync(name: "user-one", email: "user-one@example.com", password: "pass1234");
        await SeedUserAsync(name: "user-two", email: "user-two@example.com", password: "pass1234");

        var response = await Client.PostAsJsonAsync("/api/admin/users/paginated", new { page = 1, pageSize = 10 });

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        var body = await response.Content.ReadFromJsonAsync<PaginatedAdminUserResponse>();
        body.Should().NotBeNull();
        body!.Items.Should().HaveCountGreaterThanOrEqualTo(3);
        body.Page.Should().Be(1);
        body.PageSize.Should().Be(10);
        body.TotalCount.Should().BeGreaterThanOrEqualTo(3);
    }

    [Test]
    public async Task Should_AcceptIncludeDeletedParameter_When_Provided()
    {
        await AuthenticateAsAdminAsync();

        var response = await Client.PostAsJsonAsync("/api/admin/users/paginated", new { page = 1, pageSize = 10, includeDeleted = true });

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        var body = await response.Content.ReadFromJsonAsync<PaginatedAdminUserResponse>();
        body.Should().NotBeNull();
        body!.TotalCount.Should().BeGreaterThanOrEqualTo(1);
    }

    [Test]
    public async Task Should_ReturnBadRequest_When_GetUserIdIsInvalid()
    {
        await AuthenticateAsAdminAsync();

        var response = await Client.GetAsync("/api/admin/users/00000000-0000-0000-0000-000000000000");

        response.StatusCode.Should().Be(HttpStatusCode.BadRequest);
    }

    [Test]
    [LgymApi.IntegrationTests.Authorization.AuthorizationEvidence("GET", "/api/admin/users/{id}", "admin", "current-permission-allow")]
    public async Task GetUser_WhenCalledAsAdmin_ReturnsUser()
    {
        await AuthenticateAsAdminAsync();
        var user = await SeedUserAsync(name: "get-user", email: "get-user@example.com", password: "pass1234");

        var response = await Client.GetAsync($"/api/admin/users/{user.Id}");

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        var body = await response.Content.ReadFromJsonAsync<AdminUserResponse>();
        body!.Id.Should().Be(user.Id.ToString());
    }

    [Test]
    [LgymApi.IntegrationTests.Authorization.AuthorizationEvidence("POST", "/api/admin/users/{id}/update", "admin", "current-permission-allow")]
    public async Task Should_UpdateFields_When_CalledAsAdmin()
    {
        await AuthenticateAsAdminAsync();
        var user = await SeedUserAsync(name: "update-user", email: "update@example.com", password: "pass1234");

        var response = await Client.PostAsJsonAsync($"/api/admin/users/{user.Id}/update", new
        {
            name = "updated-name",
            email = "updated@example.com",
            profileRank = "Gold",
            isVisibleInRanking = true,
            avatar = "https://example.com/avatar.png"
        });

        response.StatusCode.Should().Be(HttpStatusCode.OK);

        using var scope = Factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var updated = await db.Users.FindAsync(user.Id);
        updated.Should().NotBeNull();
        updated!.Name.Should().Be("updated-name");
        updated.Email.Should().Be(new LgymApi.Domain.ValueObjects.Email("updated@example.com"));
    }

    [Test]
    public async Task Should_ReturnConflict_When_EmailIsDuplicate()
    {
        await AuthenticateAsAdminAsync();
        var user1 = await SeedUserAsync(name: "user-one", email: "one@example.com", password: "pass1234");
        var user2 = await SeedUserAsync(name: "user-two", email: "two@example.com", password: "pass1234");

        var response = await Client.PostAsJsonAsync($"/api/admin/users/{user1.Id}/update", new
        {
            name = "user-one",
            email = "two@example.com",
            profileRank = "",
            isVisibleInRanking = true,
            avatar = (string?)null
        });

        response.StatusCode.Should().Be(HttpStatusCode.Conflict);
    }

    [Test]
    [LgymApi.IntegrationTests.Authorization.AuthorizationEvidence("POST", "/api/admin/users/{id}/delete", "admin", "current-permission-allow")]
    public async Task Should_SoftDeleteUser_When_CalledAsAdmin()
    {
        await AuthenticateAsAdminAsync();
        var user = await SeedUserAsync(name: "delete-user", email: "delete@example.com", password: "pass1234");

        var response = await Client.PostAsync($"/api/admin/users/{user.Id}/delete", null);

        response.StatusCode.Should().Be(HttpStatusCode.OK);

        using var scope = Factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var deleted = await db.Users.IgnoreQueryFilters().FirstOrDefaultAsync(u => u.Id == user.Id);
        deleted.Should().NotBeNull();
        deleted!.IsDeleted.Should().BeTrue();
    }

    [Test]
    public async Task Should_ReturnBadRequest_When_DeleteUserIdIsInvalid()
    {
        await AuthenticateAsAdminAsync();

        var response = await Client.PostAsync("/api/admin/users/00000000-0000-0000-0000-000000000000/delete", null);

        response.StatusCode.Should().Be(HttpStatusCode.BadRequest);
    }

    [Test]
    [LgymApi.IntegrationTests.Authorization.AuthorizationEvidence("POST", "/api/admin/users/{id}/block", "admin", "current-permission-allow")]
    public async Task Should_BlockUser_When_CalledAsAdmin()
    {
        await AuthenticateAsAdminAsync();
        var user = await SeedUserAsync(name: "block-user", email: "block@example.com", password: "pass1234");

        var response = await Client.PostAsync($"/api/admin/users/{user.Id}/block", null);

        response.StatusCode.Should().Be(HttpStatusCode.OK);

        using var scope = Factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var blocked = await db.Users.FindAsync(user.Id);
        blocked.Should().NotBeNull();
        blocked!.IsBlocked.Should().BeTrue();
    }

    [Test]
    public async Task Should_ReturnBadRequest_When_BlockUserIdIsInvalid()
    {
        await AuthenticateAsAdminAsync();

        var response = await Client.PostAsync("/api/admin/users/00000000-0000-0000-0000-000000000000/block", null);

        response.StatusCode.Should().Be(HttpStatusCode.BadRequest);
    }

    [Test]
    [LgymApi.IntegrationTests.Authorization.AuthorizationEvidence("POST", "/api/admin/users/{id}/unblock", "admin", "current-permission-allow")]
    public async Task Should_UnblockUser_When_CalledAsAdmin()
    {
        await AuthenticateAsAdminAsync();
        var user = await SeedUserAsync(name: "unblock-user", email: "unblock@example.com", password: "pass1234");

        using (var scope = Factory.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
            var u = await db.Users.FindAsync(user.Id);
            u!.IsBlocked = true;
            await db.SaveChangesAsync();
        }

        var response = await Client.PostAsync($"/api/admin/users/{user.Id}/unblock", null);

        response.StatusCode.Should().Be(HttpStatusCode.OK);

        using (var scope = Factory.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
            var unblocked = await db.Users.FindAsync(user.Id);
            unblocked.Should().NotBeNull();
            unblocked!.IsBlocked.Should().BeFalse();
        }
    }

    [Test]
    public async Task Should_ReturnBadRequest_When_UnblockUserIdIsInvalid()
    {
        await AuthenticateAsAdminAsync();

        var response = await Client.PostAsync("/api/admin/users/00000000-0000-0000-0000-000000000000/unblock", null);

        response.StatusCode.Should().Be(HttpStatusCode.BadRequest);
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

    private sealed class AdminUserResponse
    {
        [JsonPropertyName("id")]
        public string Id { get; set; } = string.Empty;

        [JsonPropertyName("name")]
        public string Name { get; set; } = string.Empty;

        [JsonPropertyName("email")]
        public string Email { get; set; } = string.Empty;

        [JsonPropertyName("roles")]
        public List<string> Roles { get; set; } = new();
    }

    private sealed class PaginatedAdminUserResponse
    {
        [JsonPropertyName("items")]
        public List<AdminUserResponse> Items { get; set; } = new();

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
}
