using System.Net;
using System.Net.Http.Headers;
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
public sealed class RoleManagementTests : IntegrationTestBase
{
    private static readonly string[] UnknownRole = ["UnknownRole"];

    [Test]
    [LgymApi.IntegrationTests.Authorization.AuthorizationEvidence("GET", "/api/roles", "admin", "current-permission-allow")]
    public async Task RoleEndpoints_WithCurrentlyPrivilegedValidSession_ReturnOk()
    {
        // Given
        await AuthenticateAsAdminAsync();

        // When
        var response = await Client.GetAsync("/api/roles");

        // Then
        response.StatusCode.Should().Be(HttpStatusCode.OK);
    }

    [Test]
    [LgymApi.IntegrationTests.Authorization.AuthorizationEvidence("GET", "/api/roles/permission-claims", "admin", "current-permission-allow")]
    public async Task PermissionClaimsCatalog_AsAdmin_ReturnsLocalizedClaims()
    {
        await AuthenticateAsAdminAsync();

        var response = await Client.GetAsync("/api/roles/permission-claims");

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        var body = await response.Content.ReadFromJsonAsync<List<PermissionClaimResponse>>();
        body.Should().NotBeNull();
        body!.Select(c => c.ClaimValue).Should().BeEquivalentTo(AuthConstants.Permissions.All);
        body.Should().OnlyContain(c => c.ClaimType == AuthConstants.PermissionClaimType);
        body.Should().OnlyContain(c => !string.IsNullOrWhiteSpace(c.DisplayName));
    }

    [Test]
    public async Task RoleEndpoints_AsNonAdmin_ReturnForbidden()
    {
        var user = await SeedUserAsync(name: "regular-user", email: "regular-user@example.com", password: "pass1234");
        await AuthenticateAsync(user.Name, "pass1234");

        var response = await Client.GetAsync("/api/roles");

        response.StatusCode.Should().Be(HttpStatusCode.Forbidden);
    }

    [Test]
    [LgymApi.IntegrationTests.Authorization.AuthorizationEvidence("POST", "/api/roles", "admin", "current-permission-allow")]
    [LgymApi.IntegrationTests.Authorization.AuthorizationEvidence("GET", "/api/roles/{id}", "admin", "current-permission-allow")]
    public async Task CreateAndGetRole_AsAdmin_Works()
    {
        await AuthenticateAsAdminAsync();

        var createRequest = new
        {
            name = "Coach",
            description = "Coaching permissions",
            permissionClaims = new[]
            {
                AuthConstants.Permissions.ManageGlobalExercises,
                AuthConstants.Permissions.ManageAppConfig
            }
        };

        var createResponse = await Client.PostAsJsonAsync("/api/roles", createRequest);
        createResponse.StatusCode.Should().Be(HttpStatusCode.OK);

        var created = await createResponse.Content.ReadFromJsonAsync<RoleResponse>();
        created.Should().NotBeNull();
        created!.Name.Should().Be("Coach");
        created.PermissionClaims.Should().BeEquivalentTo(createRequest.permissionClaims);

        var getResponse = await Client.GetAsync($"/api/roles/{created.Id}");
        getResponse.StatusCode.Should().Be(HttpStatusCode.OK);
        var fetched = await getResponse.Content.ReadFromJsonAsync<RoleResponse>();
        fetched.Should().NotBeNull();
        fetched!.Name.Should().Be("Coach");
        fetched.PermissionClaims.Should().BeEquivalentTo(createRequest.permissionClaims);
    }

    [Test]
    [LgymApi.IntegrationTests.Authorization.AuthorizationEvidence("POST", "/api/roles/{id}/update", "admin", "current-permission-allow")]
    public async Task UpdateRole_AsAdmin_UpdatesNameAndClaims()
    {
        await AuthenticateAsAdminAsync();

        var createResponse = await Client.PostAsJsonAsync("/api/roles", new
        {
            name = "Coach",
            description = "Old",
            permissionClaims = new[] { AuthConstants.Permissions.ManageAppConfig }
        });
        var created = await createResponse.Content.ReadFromJsonAsync<RoleResponse>();

        var updateResponse = await Client.PostAsJsonAsync($"/api/roles/{created!.Id}/update", new
        {
            name = "Senior Coach",
            description = "Updated",
            permissionClaims = new[] { AuthConstants.Permissions.ManageGlobalExercises }
        });

        updateResponse.StatusCode.Should().Be(HttpStatusCode.OK);

        var getUpdatedResponse = await Client.GetAsync($"/api/roles/{created.Id}");
        var updated = await getUpdatedResponse.Content.ReadFromJsonAsync<RoleResponse>();
        updated.Should().NotBeNull();
        updated!.Name.Should().Be("Senior Coach");
        updated.Description.Should().Be("Updated");
        updated.PermissionClaims.Should().BeEquivalentTo(new[] { AuthConstants.Permissions.ManageGlobalExercises });
    }

    [Test]
    [LgymApi.IntegrationTests.Authorization.AuthorizationEvidence("POST", "/api/roles/{id}/delete", "admin", "current-permission-allow")]
    public async Task DeleteRole_AsAdmin_RemovesRole()
    {
        await AuthenticateAsAdminAsync();

        var createResponse = await Client.PostAsJsonAsync("/api/roles", new
        {
            name = "TempRole",
            description = "Temp",
            permissionClaims = Array.Empty<string>()
        });
        var created = await createResponse.Content.ReadFromJsonAsync<RoleResponse>();

        var deleteResponse = await Client.PostAsync($"/api/roles/{created!.Id}/delete", null);
        deleteResponse.StatusCode.Should().Be(HttpStatusCode.OK);

        var getResponse = await Client.GetAsync($"/api/roles/{created.Id}");
        getResponse.StatusCode.Should().Be(HttpStatusCode.NotFound);
    }

    [Test]
    [LgymApi.IntegrationTests.Authorization.AuthorizationEvidence("POST", "/api/roles/users/{id}/roles", "admin", "current-permission-allow")]
    public async Task UpdateUserRoles_AsAdmin_AssignsRolesToUser()
    {
        await AuthenticateAsAdminAsync();
        var targetUser = await SeedUserAsync(name: "target-user", email: "target-user@example.com", password: "pass1234");

        var createResponse = await Client.PostAsJsonAsync("/api/roles", new
        {
            name = "Coach",
            description = "Coach role",
            permissionClaims = new[] { AuthConstants.Permissions.ManageGlobalExercises }
        });
        var createdRole = await createResponse.Content.ReadFromJsonAsync<RoleResponse>();

        var updateResponse = await Client.PostAsJsonAsync($"/api/roles/users/{targetUser.Id}/roles", new
        {
            roles = new[] { AuthConstants.Roles.User, createdRole!.Name }
        });

        updateResponse.StatusCode.Should().Be(HttpStatusCode.OK);

        using var scope = Factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var roleNames = await db.UserRoles
            .Where(ur => ur.UserId == targetUser.Id)
            .Select(ur => ur.Role.Name)
            .ToListAsync();

        roleNames.Should().BeEquivalentTo(new[] { AuthConstants.Roles.User, "Coach" });
    }

    [Test]
    public async Task RoleEndpoints_AfterAdminDemotion_RejectPreIssuedPrivilegedToken()
    {
        // Given
        var administratorA = await SeedAdminAsync();
        var administratorB = await SeedUserAsync(
            name: "demoted-admin",
            email: "demoted-admin@example.com",
            password: "pass1234",
            isAdmin: true);
        var administratorBToken = await AuthenticateAsync(administratorB.Name, "pass1234");
        await AuthenticateAsync(administratorA.Name, AdminPassword);
        var demotionResponse = await Client.PostAsJsonAsync($"/api/roles/users/{administratorB.Id}/roles", new
        {
            roles = new[] { AuthConstants.Roles.User }
        });
        demotionResponse.StatusCode.Should().Be(HttpStatusCode.OK);
        Client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", administratorBToken);

        // When
        var response = await Client.GetAsync("/api/roles");
        var responseBody = await response.Content.ReadAsStringAsync();

        // Then
        response.StatusCode.Should().Be(
            HttpStatusCode.Forbidden,
            "the demoted administrator reused the pre-issued token and received body {0}",
            responseBody);
    }

    [Test]
    public async Task RoleEndpoints_AfterAdminPromotion_AcceptPreIssuedOrdinaryToken()
    {
        // Given
        var administrator = await SeedAdminAsync();
        var promotedUser = await SeedUserAsync(
            name: "promoted-user",
            email: "promoted-user@example.com",
            password: "pass1234");
        var promotedUserToken = await AuthenticateAsync(promotedUser.Name, "pass1234");
        await AuthenticateAsync(administrator.Name, AdminPassword);
        var promotionResponse = await Client.PostAsJsonAsync($"/api/roles/users/{promotedUser.Id}/roles", new
        {
            roles = new[] { AuthConstants.Roles.User, AuthConstants.Roles.Admin }
        });
        promotionResponse.StatusCode.Should().Be(HttpStatusCode.OK);
        Client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", promotedUserToken);

        // When
        var response = await Client.GetAsync("/api/roles");
        var responseBody = await response.Content.ReadAsStringAsync();

        // Then
        response.StatusCode.Should().Be(
            HttpStatusCode.OK,
            "the promoted user reused the pre-issued token and received body {0}",
            responseBody);
    }

    [Test]
    public async Task UpdateUserRoles_WithUnknownRole_ReturnsBadRequest()
    {
        await AuthenticateAsAdminAsync();
        var targetUser = await SeedUserAsync(name: "target-unknown", email: "target-unknown@example.com", password: "pass1234");

        var response = await Client.PostAsJsonAsync($"/api/roles/users/{targetUser.Id}/roles", new
        {
            roles = UnknownRole
        });

        response.StatusCode.Should().Be(HttpStatusCode.BadRequest);
    }

    private async Task AuthenticateAsAdminAsync()
    {
        await SeedAdminAsync();
        await AuthenticateAsync(AdminName, AdminPassword);
    }

    private async Task<string> AuthenticateAsync(string name, string password)
    {
        var loginResponse = await Client.PostAsJsonAsync("/api/login", new { name, password });
        loginResponse.StatusCode.Should().Be(HttpStatusCode.OK);
        var body = await loginResponse.Content.ReadFromJsonAsync<LoginResponse>();
        body.Should().NotBeNull();
        body!.Token.Should().NotBeNullOrWhiteSpace();

        Client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", body.Token);
        return body.Token;
    }

    private sealed class LoginResponse
    {
        [JsonPropertyName("token")]
        public string Token { get; set; } = string.Empty;
    }

    private sealed class RoleResponse
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

    private sealed class PermissionClaimResponse
    {
        [JsonPropertyName("claimType")]
        public string ClaimType { get; set; } = string.Empty;

        [JsonPropertyName("claimValue")]
        public string ClaimValue { get; set; } = string.Empty;

        [JsonPropertyName("displayName")]
        public string DisplayName { get; set; } = string.Empty;
    }
}
