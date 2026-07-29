using FluentAssertions;
using LgymApi.BackgroundWorker.Runtime;
using NUnit.Framework;

namespace LgymApi.UnitTests;

[TestFixture]
public sealed class CommandDescriptorTests
{
    [TestCaseSource(typeof(LegacyCommandContractManifest), nameof(LegacyCommandContractManifest.CommandCases))]
    public void Constructor_StoresKnownRuntimeTypeAndCanonicalId(LegacyCommandContract contract)
    {
        var registry = CommandContractRegistry.CreateDefault();
        var runtimeType = GetApplicationRuntimeType(contract);
        var descriptor = new CommandDescriptor(registry, runtimeType);

        descriptor.CanonicalId.Should().Be(contract.CanonicalId);
        descriptor.RuntimeType.Should().Be(runtimeType);
    }

    [Test]
    public void Constructor_WithNullType_ThrowsArgumentNullException()
    {
        var registry = CommandContractRegistry.CreateDefault();
        var action = () => new CommandDescriptor(registry, null!);

        action.Should().Throw<ArgumentNullException>().Which.ParamName.Should().Be("runtimeType");
    }

    [TestCaseSource(typeof(LegacyCommandContractManifest), nameof(LegacyCommandContractManifest.CommandCases))]
    public void FromPersistedId_StoresKnownCanonicalId(LegacyCommandContract contract)
    {
        var registry = CommandContractRegistry.CreateDefault();
        var descriptor = CommandDescriptor.FromPersistedId(registry, contract.CanonicalId);

        descriptor.CanonicalId.Should().Be(contract.CanonicalId);
        descriptor.RuntimeType.Should().Be(GetApplicationRuntimeType(contract));
    }

    [Test]
    public void FromPersistedId_WithNullOrWhitespace_ThrowsArgumentException()
    {
        var registry = CommandContractRegistry.CreateDefault();
        var nullAction = () => CommandDescriptor.FromPersistedId(registry, null!);
        var emptyAction = () => CommandDescriptor.FromPersistedId(registry, "");
        var whitespaceAction = () => CommandDescriptor.FromPersistedId(registry, "   ");

        nullAction.Should().Throw<ArgumentException>();
        emptyAction.Should().Throw<ArgumentException>();
        whitespaceAction.Should().Throw<ArgumentException>();
    }

    [Test]
    public void IsExactTypeMatch_UsesKnownCanonicalIdsOnly()
    {
        var firstContract = LegacyCommandContractManifest.All[0];
        var secondContract = LegacyCommandContractManifest.All[1];
        var registry = CommandContractRegistry.CreateDefault();
        var firstDescriptor = new CommandDescriptor(registry, GetApplicationRuntimeType(firstContract));
        var sameDescriptor = CommandDescriptor.FromPersistedId(registry, firstContract.FutureClrNameReadAlias);
        var differentDescriptor = new CommandDescriptor(registry, GetApplicationRuntimeType(secondContract));

        firstDescriptor.Should().Be(sameDescriptor);
        firstDescriptor.Should().NotBe(differentDescriptor);
        firstDescriptor.Equals(null).Should().BeFalse();
    }

    [TestCaseSource(typeof(LegacyCommandContractManifest), nameof(LegacyCommandContractManifest.CommandCases))]
    public void ResolveCommandType_ReadsKnownCanonicalId(LegacyCommandContract contract)
    {
        var registry = CommandContractRegistry.CreateDefault();

        CommandDescriptor.FromPersistedId(registry, contract.CanonicalId).RuntimeType
            .Should().Be(GetApplicationRuntimeType(contract));
    }

    [Test]
    public void ResolveCommandType_WithUnknownId_ThrowsInvalidOperationException()
    {
        const string unknownId = "NonExistent.Command";
        var registry = CommandContractRegistry.CreateDefault();
        var action = () => CommandDescriptor.FromPersistedId(registry, unknownId);

        action.Should().Throw<InvalidOperationException>()
            .WithMessage($"*{unknownId}*");
    }

    [Test]
    public void ResolveCommandType_WithNullOrWhitespace_ThrowsArgumentException()
    {
        var registry = CommandContractRegistry.CreateDefault();
        var nullAction = () => CommandDescriptor.FromPersistedId(registry, null!);
        var emptyAction = () => CommandDescriptor.FromPersistedId(registry, "");
        var whitespaceAction = () => CommandDescriptor.FromPersistedId(registry, "   ");

        nullAction.Should().Throw<ArgumentException>();
        emptyAction.Should().Throw<ArgumentException>();
        whitespaceAction.Should().Throw<ArgumentException>();
    }

    [TestCaseSource(typeof(LegacyCommandContractManifest), nameof(LegacyCommandContractManifest.CommandCases))]
    public void PersistenceRoundTrip_PreservesKnownCommandIdentity(LegacyCommandContract contract)
    {
        var registry = CommandContractRegistry.CreateDefault();
        var originalDescriptor = new CommandDescriptor(registry, GetApplicationRuntimeType(contract));
        var restoredDescriptor = CommandDescriptor.FromPersistedId(registry, originalDescriptor.CanonicalId);

        restoredDescriptor.RuntimeType.Should().Be(GetApplicationRuntimeType(contract));
        restoredDescriptor.Should().Be(originalDescriptor);
        restoredDescriptor.GetHashCode().Should().Be(originalDescriptor.GetHashCode());
        restoredDescriptor.ToString().Should().Be(contract.CanonicalId);
    }

    private static Type GetApplicationRuntimeType(LegacyCommandContract contract) => contract.CommandType;
}
