using System.Net;
using System.Net.Http.Json;
using FluentAssertions;
using LgymApi.Domain.Entities;
using LgymApi.Domain.ValueObjects;

namespace LgymApi.IntegrationTests;

[TestFixture]
public sealed class AdminFlagTests : IntegrationTestBase
{
    [Test]
    [LgymApi.IntegrationTests.Authorization.AuthorizationEvidence("GET", "/api/{id}/isAdmin", "authenticated-global", "ordinary-authenticated-allow")]
    public async Task IsAdmin_WithAdminUser_ReturnsTrue()
    {
        var adminUser = await SeedUserAsync(name: "adminuser", email: "admin@example.com", isAdmin: true);
        SetAuthorizationHeader(adminUser.Id);

        var response = await Client.GetAsync($"/api/{adminUser.Id}/isAdmin");

        response.StatusCode.Should().Be(HttpStatusCode.OK);

        var result = await response.Content.ReadFromJsonAsync<bool>();
        result.Should().BeTrue();
    }

    private static IEnumerable<TestCaseData> IsAdmin_FalseResultCases()
    {
        yield return new TestCaseData(
                new Func<AdminFlagTests, Task<(Id<User> authUserId, string queryId)>>(async self =>
                {
                    var user = await self.SeedUserAsync(name: "normaluser", email: "normal@example.com", isAdmin: false);
                    return (user.Id, user.Id.ToString());
                }))
            .SetName("IsAdmin_WithNonAdminUser_ReturnsFalse");

        yield return new TestCaseData(
                new Func<AdminFlagTests, Task<(Id<User> authUserId, string queryId)>>(async self =>
                {
                    var user = await self.SeedUserAsync(name: "nulladmin", email: "nulladmin@example.com");
                    return (user.Id, user.Id.ToString());
                }))
            .SetName("IsAdmin_WithNullAdminFlag_ReturnsFalse");

        yield return new TestCaseData(
                new Func<AdminFlagTests, Task<(Id<User> authUserId, string queryId)>>(async self =>
                {
                    var user = await self.SeedUserAsync(name: "authuser", email: "auth-nonexistent@example.com");
                    return (user.Id, Id<User>.New().ToString());
                }))
            .SetName("IsAdmin_WithNonExistentUser_ReturnsFalse");

        yield return new TestCaseData(
                new Func<AdminFlagTests, Task<(Id<User> authUserId, string queryId)>>(async self =>
                {
                    var user = await self.SeedUserAsync(name: "authuser", email: "auth-invalid@example.com");
                    return (user.Id, "invalid-guid");
                }))
            .SetName("IsAdmin_WithInvalidGuidFormat_ReturnsFalse");
    }

    [TestCaseSource(nameof(IsAdmin_FalseResultCases))]
    public async Task IsAdmin_WithVariousNonAdminScenarios_ReturnsFalse(
        Func<AdminFlagTests, Task<(Id<User> authUserId, string queryId)>> setupAsync)
    {
        var (authUserId, queryId) = await setupAsync(this);
        SetAuthorizationHeader(authUserId);

        var response = await Client.GetAsync($"/api/{queryId}/isAdmin");

        response.StatusCode.Should().Be(HttpStatusCode.OK);

        var result = await response.Content.ReadFromJsonAsync<bool>();
        result.Should().BeFalse();
    }
}
