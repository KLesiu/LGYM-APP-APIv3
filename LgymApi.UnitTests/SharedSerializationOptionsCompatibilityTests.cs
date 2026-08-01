using FluentAssertions;
using LgymApi.Application.Platform.Contracts.Serialization;
using LgymApi.Domain.Entities;
using LgymApi.Domain.ValueObjects;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace LgymApi.UnitTests;

[TestFixture]
public sealed class SharedSerializationOptionsCompatibilityTests
{
    [Test]
public void Current_Is_Owned_By_Platform()
    {
        var owner = typeof(SharedSerializationOptions);

        Assert.Multiple(() =>
        {
            owner.Namespace.Should().Be("LgymApi.Application.Platform.Contracts.Serialization");
owner.Assembly.GetName().Name.Should().Be("LgymApi.Platform");
        });
    }

    [Test]
    public void Current_Preserves_Persisted_Contract_Option_Values()
    {
        var options = SharedSerializationOptions.Current;

        Assert.Multiple(() =>
        {
            options.PropertyNamingPolicy.Should().Be(JsonNamingPolicy.CamelCase);
            options.DictionaryKeyPolicy.Should().Be(JsonNamingPolicy.CamelCase);
            options.DefaultIgnoreCondition.Should().Be(JsonIgnoreCondition.WhenWritingNull);
            options.PropertyNameCaseInsensitive.Should().BeTrue();
        });
    }

    [Test]
    public void Current_Registers_TypedId_Converter_Before_String_Enum_Converter()
    {
        var converters = SharedSerializationOptions.Current.Converters;

        Assert.Multiple(() =>
        {
            converters.Should().HaveCount(2);
            converters[0].Should().BeOfType<TypedIdJsonConverterFactory>();
            converters[1].Should().BeOfType<JsonStringEnumConverter>();
        });
    }

    [Test]
    public void Current_Preserves_CamelCase_Null_Omission_TypedId_And_Enum_Compatibility()
    {
        var userId = ParseUserId("a1f5f3b3-8bc9-4ec4-aedc-8070c6438bb1");
        var payload = new PersistedPayload
        {
            UserId = userId,
            Values = new Dictionary<string, string> { ["LegacyKey"] = "value" },
            Status = PersistedStatus.Ready,
            OptionalValue = null
        };

        var json = JsonSerializer.Serialize(payload, SharedSerializationOptions.Current);
        var legacyJson = "{\"UserId\":\"" + userId + "\",\"Values\":{\"LegacyKey\":\"value\"},\"Status\":1,\"OptionalValue\":null}";
        var legacyPayload = JsonSerializer.Deserialize<PersistedPayload>(legacyJson, SharedSerializationOptions.Current);

        Assert.Multiple(() =>
        {
            json.Should().Be("{\"userId\":\"" + userId + "\",\"values\":{\"legacyKey\":\"value\"},\"status\":\"Ready\"}");
            legacyPayload.Should().NotBeNull();
            legacyPayload!.UserId.Should().Be(userId);
            legacyPayload.Values.Should().ContainSingle().Which.Should().Be(new KeyValuePair<string, string>("LegacyKey", "value"));
            legacyPayload.Status.Should().Be(PersistedStatus.Ready);
            legacyPayload.OptionalValue.Should().BeNull();
        });
    }

    [Test]
    public void Current_Rejects_Malformed_TypedId_And_Enum_Values()
    {
        Action invalidTypedId = () => JsonSerializer.Deserialize<PersistedPayload>(
            "{\"userId\":\"not-a-guid\",\"status\":\"Ready\"}",
            SharedSerializationOptions.Current);
        Action invalidEnum = () => JsonSerializer.Deserialize<PersistedPayload>(
            "{\"userId\":\"a1f5f3b3-8bc9-4ec4-aedc-8070c6438bb1\",\"status\":\"UnknownLegacyStatus\"}",
            SharedSerializationOptions.Current);

        Assert.Multiple(() =>
        {
            invalidTypedId.Should().Throw<JsonException>();
            invalidEnum.Should().Throw<JsonException>();
        });
    }

    private static Id<User> ParseUserId(string value)
    {
        Id<User>.TryParse(value, out var userId).Should().BeTrue();
        return userId;
    }

    private sealed class PersistedPayload
    {
        public Id<User> UserId { get; init; }

        public Dictionary<string, string> Values { get; init; } = new();

        public PersistedStatus Status { get; init; }

        public string? OptionalValue { get; init; }
    }

    private enum PersistedStatus
    {
        Unknown = 0,
        Ready = 1
    }
}
