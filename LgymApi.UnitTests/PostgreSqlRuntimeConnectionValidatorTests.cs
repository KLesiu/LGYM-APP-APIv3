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
    public void ValidateInspection_WhenProtectedTablePoliciesDiffer_FailsClosed()
    {
        var options = new PostgreSqlRuntimeValidationOptions
        {
            ExpectedDatabase = "lgym",
            ExpectedRole = "lgym_runtime",
            ProtectedTables = [new PostgreSqlProtectedTableOptions
            {
                Name = "UserTutorialProgresses",
                Policies = [new PostgreSqlPolicyOptions { Name = "tutorial_owner", Command = "SELECT" }]
            }]
        };

        var action = () => PostgreSqlRuntimeConnectionValidator.ValidateInspection(CreateInspection(), options);

        action.Should().Throw<InvalidOperationException>().WithMessage("*policies do not match*");
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

    private static PostgreSqlRuntimeInspection CreateInspection()
        => new(
            "lgym",
            "lgym_runtime",
            false,
            false,
            [],
            false,
            true,
            [],
            [],
            [new PostgreSqlProtectedTableInspection("public.UserTutorialProgresses", false, false, false, [])],
            null);
}
