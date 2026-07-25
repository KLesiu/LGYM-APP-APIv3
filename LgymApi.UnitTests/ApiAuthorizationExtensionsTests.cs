using System.Security.Claims;
using FluentAssertions;
using LgymApi.Api.Configuration;
using LgymApi.Domain.Security;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Authorization.Infrastructure;
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
                .OfType<ClaimsAuthorizationRequirement>()
                .Should()
                .ContainSingle()
                .Which
                .ClaimType
                .Should()
                .Be(AuthConstants.PermissionClaimType);
            policy.Requirements
                .OfType<ClaimsAuthorizationRequirement>()
                .Single()
                .AllowedValues
                .Should()
                .Equal(permission);
        }

        ExpectedPolicies.Should().HaveCount(5);
    }

    [TestCaseSource(nameof(GetPolicyCases))]
    public async Task AddApiAuthorizationPolicies_GrantsOnlyItsMatchingPermission(string policyName, string permission)
    {
        using var serviceProvider = CreateServiceProvider();
        var authorizationService = serviceProvider.GetRequiredService<IAuthorizationService>();

        var permitted = await authorizationService.AuthorizeAsync(CreatePrincipal(permission), null, policyName);
        var denied = await authorizationService.AuthorizeAsync(CreatePrincipal(GetDifferentPermission(permission)), null, policyName);

        permitted.Succeeded.Should().BeTrue();
        denied.Succeeded.Should().BeFalse();
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

    private static string GetDifferentPermission(string permission)
    {
        return AuthConstants.Permissions.All.First(candidate => candidate != permission);
    }
}
