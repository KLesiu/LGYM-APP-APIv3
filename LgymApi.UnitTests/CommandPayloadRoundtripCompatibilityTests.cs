using FluentAssertions;
using LgymApi.Application.Platform.Contracts.Serialization;
using NUnit.Framework;
using System.Text.Json;

namespace LgymApi.UnitTests;

[TestFixture]
public sealed class CommandPayloadRoundtripCompatibilityTests
{
    [TestCaseSource(typeof(LegacyCommandContractManifest), nameof(LegacyCommandContractManifest.CommandCases))]
    public void FixedGoldenPayload_RoundTripsByteForByte_ForAllFifteenCommands(LegacyCommandContract contract)
    {
        var command = JsonSerializer.Deserialize(
            contract.PayloadJson,
            contract.CommandType,
            SharedSerializationOptions.Current);

        command.Should().NotBeNull().And.BeOfType(contract.CommandType);
        JsonSerializer.Serialize(command, contract.CommandType, SharedSerializationOptions.Current)
            .Should().Be(contract.PayloadJson);
    }

    [TestCaseSource(nameof(LegacyPropertyCaseFixtures))]
    public void LegacyPascalOrMixedCasePayload_RemainsReadable(
        string commandName,
        string legacyPayloadJson)
    {
        var contract = LegacyCommandContractManifest.All.Single(row => row.Name == commandName);

        var command = JsonSerializer.Deserialize(
            legacyPayloadJson,
            contract.CommandType,
            SharedSerializationOptions.Current);

        JsonSerializer.Serialize(command, contract.CommandType, SharedSerializationOptions.Current)
            .Should().Be(contract.PayloadJson);
    }

    [Test]
    public void SharedSerializationOptions_RemainsCompatibleWithGoldenPayloadFixtures()
    {
        var options = SharedSerializationOptions.Current;

        options.PropertyNamingPolicy.Should().Be(JsonNamingPolicy.CamelCase);
        options.DictionaryKeyPolicy.Should().Be(JsonNamingPolicy.CamelCase);
        options.PropertyNameCaseInsensitive.Should().BeTrue();
    }

    private static IEnumerable<TestCaseData> LegacyPropertyCaseFixtures()
    {
        yield return new TestCaseData(
                "UserRegistered",
                "{\"UserId\":\"00000000-0000-0000-0000-000000000001\"}")
            .SetName("UserRegistered_legacy_PascalCase");

        yield return new TestCaseData(
                "TrainingCompleted",
                "{\"userId\":\"00000000-0000-0000-0000-000000000002\",\"TrainingId\":\"00000000-0000-0000-0000-000000000003\"}")
            .SetName("TrainingCompleted_legacy_mixed_case");

        yield return new TestCaseData(
                "InvitationCreated",
                "{\"InvitationId\":\"00000000-0000-0000-0000-000000000004\"}")
            .SetName("InvitationCreated_legacy_PascalCase");
    }
}
