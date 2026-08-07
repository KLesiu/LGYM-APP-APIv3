using System.Security.Claims;
using FluentAssertions;
using LgymApi.Api.Authorization;
using LgymApi.Api.Configuration;
using LgymApi.Api.Middleware;
using LgymApi.Domain.Security;
using LgymApi.Domain.ValueObjects;
using LgymApi.Identity.Contracts;
using LgymApi.Identity.Contracts.Accounts;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.DependencyInjection;
using NUnit.Framework;

namespace LgymApi.UnitTests;

[TestFixture]
public sealed class ApiAuthorizationExtensionsTests
{
    private static readonly IReadOnlyDictionary<string, string> ExpectedPolicies =
        new Dictionary<string, string>(StringComparer.Ordinal)
        {
            [AuthConstants.Policies.AdminAccess] = AuthConstants.Permissions.AdminAccess,
            [AuthConstants.Policies.ManageUserRoles] = AuthConstants.Permissions.ManageUserRoles,
            [AuthConstants.Policies.ManageAppConfig] = AuthConstants.Permissions.ManageAppConfig,
            [AuthConstants.Policies.ManageGlobalExercises] = AuthConstants.Permissions.ManageGlobalExercises,
            [AuthConstants.Policies.TrainerAccess] = AuthConstants.Permissions.TrainerAccess
        };

    [Test]
    public async Task AddApiAuthorizationPolicies_RegistersOnlyTheCanonicalPermissionPolicies()
    {
        using var serviceProvider = CreateServiceProvider();
        var policyProvider = serviceProvider.GetRequiredService<IAuthorizationPolicyProvider>();

        foreach (var (policyName, permission) in ExpectedPolicies)
        {
            var policy = await policyProvider.GetPolicyAsync(policyName);

            policy.Should().NotBeNull();
            policy!.Requirements
                .OfType<CurrentPermissionRequirement>()
                .Should()
                .ContainSingle()
                .Which
                .Permission
                .Should()
                .Be(permission);
        }

        ExpectedPolicies.Should().HaveCount(5);
    }

    [TestCaseSource(nameof(GetPolicyCases))]
    public async Task AddApiAuthorizationPolicies_GrantsOnlyItsMatchingPermission(string policyName, string permission)
    {
        using var serviceProvider = CreateServiceProvider();
        var authorizationService = serviceProvider.GetRequiredService<IAuthorizationService>();

        var differentPermission = GetDifferentPermission(permission);
        var permitted = await authorizationService.AuthorizeAsync(
            CreatePrincipal(differentPermission),
            CreateHttpContext(permission),
            policyName);
        var denied = await authorizationService.AuthorizeAsync(
            CreatePrincipal(permission),
            CreateHttpContext(differentPermission),
            policyName);

        permitted.Succeeded.Should().BeTrue();
        denied.Succeeded.Should().BeFalse();
    }

    [Test]
    public async Task AddApiAuthorizationPolicies_DeniesTokenPermissionWhenFreshContextFeatureIsAbsent()
    {
        using var serviceProvider = CreateServiceProvider();
        var authorizationService = serviceProvider.GetRequiredService<IAuthorizationService>();

        var result = await authorizationService.AuthorizeAsync(
            CreatePrincipal(AuthConstants.Permissions.AdminAccess),
            new DefaultHttpContext(),
            AuthConstants.Policies.AdminAccess);

        result.Succeeded.Should().BeFalse();
    }

    private static IEnumerable<TestCaseData> GetPolicyCases()
    {
        return ExpectedPolicies.Select(policy => new TestCaseData(policy.Key, policy.Value));
    }

    private static ServiceProvider CreateServiceProvider()
    {
        var services = new ServiceCollection();
        services.AddLogging();
        services.AddApiAuthorizationPolicies();
        return services.BuildServiceProvider();
    }

    private static ClaimsPrincipal CreatePrincipal(string permission)
    {
        return new ClaimsPrincipal(new ClaimsIdentity(
        [new Claim(AuthConstants.PermissionClaimType, permission)]));
    }

    private static HttpContext CreateHttpContext(string permission)
    {
        var httpContext = new DefaultHttpContext();
        httpContext.Features.Set<IAuthenticatedAccountContextFeature>(
            new TestAuthenticatedAccountContextFeature(
                new AuthenticatedAccountContext(
                    Id<AccountReference>.New(),
                    Id<AccountSessionReference>.New(),
                    [],
                    [permission],
                    IsBlocked: false,
                    IsDeleted: false)));
        return httpContext;
    }

    private static string GetDifferentPermission(string permission)
    {
        return AuthConstants.Permissions.All.First(candidate => candidate != permission);
    }

    private sealed record TestAuthenticatedAccountContextFeature(AuthenticatedAccountContext Context)
        : IAuthenticatedAccountContextFeature;
}
