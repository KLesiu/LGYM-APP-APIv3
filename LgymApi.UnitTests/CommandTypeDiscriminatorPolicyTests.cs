using FluentAssertions;
using LgymApi.BackgroundWorker.Runtime;
using NUnit.Framework;

namespace LgymApi.UnitTests;

[TestFixture]
public sealed class CommandTypeDiscriminatorPolicyTests
{
    [TestCaseSource(typeof(LegacyCommandContractManifest), nameof(LegacyCommandContractManifest.CommandCases))]
    public void GetDiscriminator_WritesFixedCanonicalId_ForKnownCommand(LegacyCommandContract contract)
    {
        var policy = new CommandTypeDiscriminatorPolicy(CommandContractRegistry.CreateDefault());

        policy.GetDiscriminator(GetApplicationRuntimeType(contract))
            .Should().Be(contract.CanonicalId);
    }

    [TestCaseSource(typeof(LegacyCommandContractManifest), nameof(LegacyCommandContractManifest.CommandCases))]
    public void ResolveType_ReadsFixedCanonicalId_ForKnownCommand(LegacyCommandContract contract)
    {
        var policy = new CommandTypeDiscriminatorPolicy(CommandContractRegistry.CreateDefault());

        policy.ResolveType(contract.CanonicalId)
            .Should().Be(GetApplicationRuntimeType(contract));
    }

    [Test]
    public void ResolveType_Throws_ForUnknownDurableCommandId()
    {
        var policy = new CommandTypeDiscriminatorPolicy(CommandContractRegistry.CreateDefault());
        var action = () => policy.ResolveType("Fake.Namespace.UnknownCommand");

        action.Should().Throw<InvalidOperationException>();
    }

    [TestCaseSource(typeof(LegacyCommandContractManifest), nameof(LegacyCommandContractManifest.CommandCases))]
    public void IsExactMatch_CanonicalizesReadAliasBeforeComparison(LegacyCommandContract contract)
    {
        var policy = new CommandTypeDiscriminatorPolicy(CommandContractRegistry.CreateDefault());

        policy.IsExactMatch(contract.CanonicalId, contract.FutureClrNameReadAlias)
            .Should().BeTrue();
    }

    [Test]
    public void IsExactMatch_ReturnsFalse_ForNullOrWhitespace()
    {
        var policy = new CommandTypeDiscriminatorPolicy(CommandContractRegistry.CreateDefault());

        policy.IsExactMatch("Known.Command", "").Should().BeFalse();
        policy.IsExactMatch("", "Known.Command").Should().BeFalse();
    }

    private static Type GetApplicationRuntimeType(LegacyCommandContract contract) => contract.CommandType;
}
