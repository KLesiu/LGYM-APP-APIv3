using FluentAssertions;
using LgymApi.Infrastructure.Data;

namespace LgymApi.UnitTests;

[TestFixture]
public sealed class PostgreSqlRuntimeConnectionValidatorTests
{
    [Test]
    public void ValidateInspection_WhenRuntimeStateIsSafe_Succeeds()
    {
        var action = () => PostgreSqlRuntimeConnectionValidator.ValidateInspection(CreateInspection(), CreateOptions());

        action.Should().NotThrow();
    }

    [TestCase("wrong_database", "lgym_runtime")]
    [TestCase("lgym", "wrong_role")]
    public void ValidateInspection_WhenDatabaseOrRoleDoesNotMatch_FailsClosed(string databaseName, string currentUser)
    {
        var inspection = CreateInspection() with { DatabaseName = databaseName, CurrentUser = currentUser };

        var action = () => PostgreSqlRuntimeConnectionValidator.ValidateInspection(inspection, CreateOptions());

        action.Should().Throw<InvalidOperationException>();
    }

    [Test]
    public void ValidateInspection_WhenRuntimeRoleHasElevatedMembership_FailsClosed()
    {
        var inspection = CreateInspection() with { ElevatedMemberships = ["lgym_maintenance"] };

        var action = () => PostgreSqlRuntimeConnectionValidator.ValidateInspection(inspection, CreateOptions());

        action.Should().Throw<InvalidOperationException>().WithMessage("*membership*");
    }

    [Test]
    public void ValidateInspection_WhenRuntimeRoleIsSuperuser_FailsClosed()
    {
        var action = () => PostgreSqlRuntimeConnectionValidator.ValidateInspection(
            CreateInspection() with { IsSuperuser = true },
            CreateOptions());

        action.Should().Throw<InvalidOperationException>().WithMessage("*superuser*");
    }

    [Test]
    public void ValidateInspection_WhenRuntimeRoleBypassesRowSecurity_FailsClosed()
    {
        var action = () => PostgreSqlRuntimeConnectionValidator.ValidateInspection(
            CreateInspection() with { BypassesRowSecurity = true },
            CreateOptions());

        action.Should().Throw<InvalidOperationException>().WithMessage("*BYPASSRLS*");
    }

    [Test]
    public void ValidateInspection_WhenRuntimeConnectionUsesMultiplexing_FailsClosed()
    {
        var inspection = CreateInspection() with { MultiplexingEnabled = true };

        var action = () => PostgreSqlRuntimeConnectionValidator.ValidateInspection(inspection, CreateOptions());

        action.Should().Throw<InvalidOperationException>().WithMessage("*multiplexing*");
    }

    [Test]
    public void ValidateInspection_WhenRuntimeRoleOwnsProtectedTable_FailsClosed()
    {
        var inspection = CreateInspection() with
        {
            ProtectedTables = [new PostgreSqlProtectedTableInspection("public.UserTutorialProgresses", true, false, true, [])]
        };

        var action = () => PostgreSqlRuntimeConnectionValidator.ValidateInspection(inspection, CreateOptions());

        action.Should().Throw<InvalidOperationException>().WithMessage("*must not own protected tables*");
    }

    [Test]
    public void ValidateInspection_WhenProtectedTableRlsStateDiffers_FailsClosed()
    {
        var inspection = CreateInspection() with
        {
            ProtectedTables = [new PostgreSqlProtectedTableInspection("public.UserTutorialProgresses", true, true, false, [])]
        };

        var action = () => PostgreSqlRuntimeConnectionValidator.ValidateInspection(inspection, CreateOptions());

        action.Should().Throw<InvalidOperationException>().WithMessage("*RLS state*");
    }

    [Test]
    public void ValidateInspection_WhenPolicyRolesHaveSeparateIdentityButEqualOrdinalSet_Succeeds()
    {
        var expectedPolicy = CreatePolicyOptions("tutorial_owner");
        expectedPolicy.Roles.Add("lgym_runtime");
        var options = new PostgreSqlRuntimeValidationOptions
        {
            ExpectedDatabase = "lgym",
            ExpectedRole = "lgym_runtime",
            ProtectedTables = [new PostgreSqlProtectedTableOptions
            {
                Name = "UserTutorialProgresses",
                Policies = [expectedPolicy]
            }]
        };
        var inspection = CreateInspectionWithPolicies([CreatePolicyInspection()]);

        var action = () => PostgreSqlRuntimeConnectionValidator.ValidateInspection(inspection, options);

        action.Should().NotThrow();
    }

    [TestCase(PolicyDrift.Missing)]
    [TestCase(PolicyDrift.Extra)]
    [TestCase(PolicyDrift.Name)]
    [TestCase(PolicyDrift.RoleSet)]
    [TestCase(PolicyDrift.RoleCase)]
    [TestCase(PolicyDrift.DuplicateRole)]
    [TestCase(PolicyDrift.Command)]
    [TestCase(PolicyDrift.Permissiveness)]
    [TestCase(PolicyDrift.Using)]
    [TestCase(PolicyDrift.WithCheck)]
    public void ValidateInspection_WhenPolicyContractDrifts_FailsClosed(PolicyDrift drift)
    {
        var expectedPolicy = CreatePolicyOptions("tutorial_owner");
        expectedPolicy.Roles.Add("lgym_runtime");
        var options = new PostgreSqlRuntimeValidationOptions
        {
            ExpectedDatabase = "lgym",
            ExpectedRole = "lgym_runtime",
            ProtectedTables = [new PostgreSqlProtectedTableOptions
            {
                Name = "UserTutorialProgresses",
                Policies = [expectedPolicy]
            }]
        };
        var policy = CreatePolicyInspection();
        IReadOnlyList<PostgreSqlPolicyInspection> actualPolicies = drift switch
        {
            PolicyDrift.Missing => [],
            PolicyDrift.Extra => [policy, policy with { Name = "unexpected_policy" }],
            PolicyDrift.Name => [policy with { Name = "renamed_policy" }],
            PolicyDrift.RoleSet => [policy with { Roles = ["PUBLIC"] }],
            PolicyDrift.RoleCase => [policy with { Roles = ["PUBLIC", "LGYM_RUNTIME"] }],
            PolicyDrift.DuplicateRole => [policy with { Roles = ["PUBLIC", "lgym_runtime", "lgym_runtime"] }],
            PolicyDrift.Command => [policy with { Command = "DELETE" }],
            PolicyDrift.Permissiveness => [policy with { IsPermissive = false }],
            PolicyDrift.Using => [policy with { Using = "ActorOwnsParent" }],
            PolicyDrift.WithCheck => [policy with { WithCheck = "ActorOwnsParent" }],
            _ => throw new ArgumentOutOfRangeException(nameof(drift), drift, null)
        };
        var inspection = CreateInspectionWithPolicies(actualPolicies);

        var action = () => PostgreSqlRuntimeConnectionValidator.ValidateInspection(inspection, options);

        action.Should().Throw<InvalidOperationException>().WithMessage("*policies do not match*");
    }

    [Test]
    public void ValidateOptions_WhenPolicySemanticContractIsIncomplete_FailsClosed()
    {
        var options = new PostgreSqlRuntimeValidationOptions
        {
            ExpectedDatabase = "lgym",
            ExpectedRole = "lgym_runtime",
            ProtectedTables = [new PostgreSqlProtectedTableOptions
            {
                Name = "UserTutorialProgresses",
                Policies = [new PostgreSqlPolicyOptions
                {
                    Name = "tutorial_owner",
                    Command = "SELECT"
                }]
            }]
        };

        var inspection = CreateInspection() with
        {
            ProtectedTables = [new PostgreSqlProtectedTableInspection(
                "public.UserTutorialProgresses",
                false,
                false,
                false,
                [new PostgreSqlPolicyInspection(
                    "tutorial_owner",
                    "SELECT",
                    ["PUBLIC"],
                    true,
                    "ActorOwnsRow",
                    null)])]
        };
        var action = () => PostgreSqlRuntimeConnectionValidator.ValidateInspection(inspection, options);

        action.Should().Throw<InvalidOperationException>();
    }

    [Test]
    public void ValidateInspection_WhenRuntimeGrantsAreUnsafe_FailsClosed()
    {
        var inspection = CreateInspection() with
        {
            MissingTableGrants = ["public.Users"]
        };

        var action = () => PostgreSqlRuntimeConnectionValidator.ValidateInspection(inspection, CreateOptions());

        action.Should().Throw<InvalidOperationException>().WithMessage("*privileges*");
    }

    [Test]
    public void ValidateInspection_WhenHelperSecurityModeOrGrantIsUnsafe_FailsClosed()
    {
        var options = new PostgreSqlRuntimeValidationOptions
        {
            ExpectedDatabase = "lgym",
            ExpectedRole = "lgym_runtime",
            ProtectedTables = [new PostgreSqlProtectedTableOptions { Name = "UserTutorialProgresses" }],
            HelperFunction = new PostgreSqlHelperFunctionOptions { Name = "current_actor" }
        };
        var inspection = CreateInspection() with
        {
            HelperFunction = new PostgreSqlHelperFunctionInspection(true, false, false)
        };

        var action = () => PostgreSqlRuntimeConnectionValidator.ValidateInspection(inspection, options);

        action.Should().Throw<InvalidOperationException>().WithMessage("*helper function*");
    }

    private static PostgreSqlRuntimeValidationOptions CreateOptions()
        => new()
        {
            ExpectedDatabase = "lgym",
            ExpectedRole = "lgym_runtime",
            ProtectedTables = [new PostgreSqlProtectedTableOptions { Name = "UserTutorialProgresses" }]
        };

    private static PostgreSqlPolicyOptions CreatePolicyOptions(string name)
        => new()
        {
            Name = name,
            Command = "SELECT",
            Roles = ["PUBLIC"],
            IsPermissive = true,
            Using = "ActorOwnsRow",
            WithCheck = null
        };

    private static PostgreSqlPolicyInspection CreatePolicyInspection()
        => new("tutorial_owner", "SELECT", ["lgym_runtime", "PUBLIC"], true, "ActorOwnsRow", null);

    private static PostgreSqlRuntimeInspection CreateInspectionWithPolicies(
        IReadOnlyList<PostgreSqlPolicyInspection> policies)
        => CreateInspection() with
        {
            ProtectedTables = [new PostgreSqlProtectedTableInspection(
                "public.UserTutorialProgresses",
                false,
                false,
                false,
                policies)]
        };

    private static PostgreSqlRuntimeInspection CreateInspection()
        => new(
            "lgym",
            "lgym_runtime",
            false,
            false,
            [],
            false,
            true,
            true,
            [],
            [],
            [new PostgreSqlProtectedTableInspection("public.UserTutorialProgresses", false, false, false, [])],
            null);

    public enum PolicyDrift
    {
        Missing,
        Extra,
        Name,
        RoleSet,
        RoleCase,
        DuplicateRole,
        Command,
        Permissiveness,
        Using,
        WithCheck
    }
}
