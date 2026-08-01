using LgymApi.Domain.ValueObjects;
using System.Text.Json;
using System.Text.Json.Serialization;
using LgymApi.Application.Platform.Contracts.Serialization;
using NUnit.Framework;
using FluentAssertions;

namespace LgymApi.UnitTests;

[TestFixture]
public sealed class TypedIdTests
{
    // Test fixtures to act as entity types
    private class User { }
    private class Plan { }

    // Helper to parse fixed test IDs from UUID string literals
    private static Id<T> ParseTestId<T>(string uuid)
    {
        if (!Id<T>.TryParse(uuid, out var id))
        {
            throw new ArgumentException($"Invalid UUID: {uuid}", nameof(uuid));
        }
        return id;
    }

    #region Equality and Default Behavior

    [Test]
    public void RecordStruct_ImplementsValueEquality()
    {
        var id1 = ParseTestId<User>("00000000-0000-0000-0000-000000000001");
        var id2 = ParseTestId<User>("00000000-0000-0000-0000-000000000001");

        id1.Should().Be(id2);
    }

    [Test]
    public void RecordStruct_DifferentiatesDifferentValues()
    {
        var id1 = ParseTestId<User>("00000000-0000-0000-0000-000000000001");
        var id2 = ParseTestId<User>("00000000-0000-0000-0000-000000000002");

        id1.Should().NotBe(id2);
    }

    [Test]
    public void Empty_ReturnsGuidEmpty()
    {
        var empty = Id<User>.Empty;

        empty.IsEmpty.Should().BeTrue();
        empty.Should().Be(default(Id<User>));
    }

    [Test]
    public void Default_ReturnsEmptyGuid()
    {
        Id<User> id = default;

        id.IsEmpty.Should().BeTrue();
        id.Should().Be(Id<User>.Empty);
    }

    [Test]
    public void Nullable_AllowsNullCheck()
    {
        Id<User>? id = null;

        id.Should().BeNull();
    }

    [Test]
    public void Nullable_AllowsNonNullValue()
    {
        var id = Id<User>.New();
        Id<User>? nullableId = id;

        nullableId.Should().NotBeNull();
        nullableId.Value.Should().Be(id);
    }

    [Test]
    public void Baseline_ValueEquality_HashAndNullableJsonSemantics_AreStable()
    {
        var first = ParseTestId<User>("00000000-0000-0000-0000-000000000003");
        var same = ParseTestId<User>("00000000-0000-0000-0000-000000000003");
        Id<User>? missing = null;

        first.Should().Be(same);
        first.GetHashCode().Should().Be(same.GetHashCode());
        missing.Should().BeNull();
        JsonSerializer.Serialize(missing, _serializerOptions).Should().Be("null");
        JsonSerializer.Deserialize<Id<User>?>("null", _serializerOptions).Should().BeNull();
    }

    #endregion

    #region New() Factory

    [Test]
    public void New_ProducesNonEmptyId()
    {
        var id = Id<User>.New();

        id.IsEmpty.Should().BeFalse();
        id.Should().NotBe(Id<User>.Empty);
    }

    [Test]
    public void New_ProducesDifferentIdEachCall()
    {
        var id1 = Id<User>.New();
        var id2 = Id<User>.New();

        id1.Should().NotBe(id2);
    }

    #endregion

    #region Explicit Conversions (No Implicit Conversions)

    [Test]
    public void ExplicitOperator_UnwrapsGuid()
    {
        var id = ParseTestId<User>("00000000-0000-0000-0000-000000000099");
        var unwrapped = id.GetValue();

        unwrapped.ToString().Should().Be("00000000-0000-0000-0000-000000000099");
    }

    [Test]
    public void ExplicitOperator_WrapsGuid()
    {
        var id = ParseTestId<User>("00000000-0000-0000-0000-000000000099");

        id.ToString().Should().Be("00000000-0000-0000-0000-000000000099");
    }

    [Test]
    public void NoImplicitConversion_GuidToId()
    {
        // This test verifies that implicit conversion is not possible
        // If this compiles, the design is violated.
        // If this does not compile, the test passes.
#pragma warning disable CS0219 // Variable is assigned but its value is never used
        var id = Id<User>.New();
        // Uncomment the following line to verify compilation fails:
        // Id<User> id2 = id.GetValue(); // This should NOT compile
#pragma warning restore CS0219

        typeof(Id<User>).GetMethods().Any(method => method.Name == "op_Implicit").Should().BeFalse();
    }

    [Test]
    public void NoImplicitConversion_IdToGuid()
    {
        // This test verifies that implicit conversion is not possible
        // If this compiles, the design is violated.
        // If this does not compile, the test passes.
#pragma warning disable CS0219 // Variable is assigned but its value is never used
        var id = Id<User>.New();
        // Uncomment the following line to verify compilation fails:
        // var unwrapped = id; // Cannot assign Id<T> to Guid implicitly
#pragma warning restore CS0219

        typeof(Id<User>).GetMethods().Any(method => method.Name == "op_Implicit").Should().BeFalse();
    }

    #endregion

    #region Type Safety (Compile-Time Separation)

    [Test]
    public void DifferentEntityTypes_AreCompiledAsDifferentTypes()
    {
        var userId = Id<User>.New();
        var planId = Id<Plan>.New();

        // userId and planId have different types at compile time
        // This test documents that intent even though runtime equates them if underlying value is same
        userId.GetType().Name.Should().Be("Id`1");
        planId.GetType().Name.Should().Be("Id`1");

        // At runtime they are technically different types (generic instantiation)
        typeof(Id<User>).Should().NotBe(typeof(Id<Plan>));
    }

    #endregion

    #region String Representation

    [Test]
    public void ToString_ReturnsGuidString()
    {
        var id = ParseTestId<User>("00000000-0000-0000-0000-000000000077");

        id.ToString().Should().Be("00000000-0000-0000-0000-000000000077");
    }

    #endregion

    #region Value Property

    [Test]
    public void Value_Property_ExposesSingleGuidField()
    {
        var id = Id<User>.New();
        var value = id.GetValue();

        value.ToString().Length.Should().Be(36); // Standard UUID format
    }

    #endregion

    #region IsEmpty Property

    [Test]
    public void IsEmpty_ReturnsTrueForGuidEmpty()
    {
        var id = Id<User>.Empty;

        id.IsEmpty.Should().BeTrue();
    }

    [Test]
    public void IsEmpty_ReturnsFalseForNonEmptyGuid()
    {
        var id = Id<User>.New();

        id.IsEmpty.Should().BeFalse();
    }

    #endregion

    #region JSON Serialization (Converter Factory)

    private readonly JsonSerializerOptions _serializerOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
        Converters = { new TypedIdJsonConverterFactory() }
    };

    [Test]
    public void Serialize_ProducesGuidString()
    {
        var id = ParseTestId<User>("00000000-0000-0000-0000-000000000042");

        var json = JsonSerializer.Serialize(id, _serializerOptions);

        json.Should().Contain("00000000-0000-0000-0000-000000000042");
        // JSON string is quoted
        json.Should().Be("\"00000000-0000-0000-0000-000000000042\"");
    }

    [Test]
    public void Deserialize_ParsesGuidString()
    {
        var guidString = "\"00000000-0000-0000-0000-000000000043\"";

        var id = JsonSerializer.Deserialize<Id<User>>(guidString, _serializerOptions);

        id.ToString().Should().Be("00000000-0000-0000-0000-000000000043");
    }

    [Test]
    public void RoundTrip_SerializeDeserialize_PreservesValue()
    {
        var originalId = Id<User>.New();

        var json = JsonSerializer.Serialize(originalId, _serializerOptions);
        var deserialized = JsonSerializer.Deserialize<Id<User>>(json, _serializerOptions);

        deserialized.Should().Be(originalId);
    }

    [Test]
    public void RoundTrip_WithObject_PreservesId()
    {
        var userId = Id<User>.New();
        var original = new UserDto { Id = userId };

        var json = JsonSerializer.Serialize(original, _serializerOptions);
        var deserialized = JsonSerializer.Deserialize<UserDto>(json, _serializerOptions);

        deserialized!.Id.Should().Be(userId);
    }

    [Test]
    public void Deserialize_WithInvalidUuid_ThrowsJsonException()
    {
        var invalidJson = "\"not-a-uuid\"";

        var ex = FluentActions.Invoking(() =>
            JsonSerializer.Deserialize<Id<User>>(invalidJson, _serializerOptions)).Should().Throw<JsonException>().Which;

        ex.Message.Should().Contain("Invalid GUID format");
    }

    [Test]
    public void Deserialize_WithEmptyString_ThrowsJsonException()
    {
        var emptyJson = "\"\"";

        var ex = FluentActions.Invoking(() =>
            JsonSerializer.Deserialize<Id<User>>(emptyJson, _serializerOptions)).Should().Throw<JsonException>().Which;

        ex.Message.Should().Contain("cannot be empty");
    }

    [Test]
    public void Deserialize_WithNullToken_ThrowsJsonException()
    {
        var nullJson = "null";

        var ex = FluentActions.Invoking(() =>
            JsonSerializer.Deserialize<Id<User>>(nullJson, _serializerOptions)).Should().Throw<JsonException>().Which;

        ex.Message.Should().Contain("cannot be null");
    }

    [Test]
    public void Deserialize_WithNumberToken_ThrowsJsonException()
    {
        var numberJson = "123";

        var ex = FluentActions.Invoking(() =>
            JsonSerializer.Deserialize<Id<User>>(numberJson, _serializerOptions)).Should().Throw<JsonException>().Which;

        ex.Message.Should().Contain("Expected string token");
    }

    #endregion

    #region JSON Serialization (Nullable)

    [Test]
    public void Serialize_NullableId_WithValue_ProducesGuidString()
    {
        Id<User>? id = ParseTestId<User>("00000000-0000-0000-0000-000000000044");

        var json = JsonSerializer.Serialize(id, _serializerOptions);

        json.Should().Be("\"00000000-0000-0000-0000-000000000044\"");
    }

    [Test]
    public void Serialize_NullableId_WithNull_ProducesNull()
    {
        Id<User>? id = null;

        var json = JsonSerializer.Serialize(id, _serializerOptions);

        json.Should().Be("null");
    }

    [Test]
    public void Deserialize_NullableId_WithGuidString_ProducesValue()
    {
        var guidJson = "\"00000000-0000-0000-0000-000000000045\"";

        var id = JsonSerializer.Deserialize<Id<User>?>(guidJson, _serializerOptions);

        id.Should().NotBeNull();
        id!.Value.ToString().Should().Be("00000000-0000-0000-0000-000000000045");
    }

    [Test]
    public void Deserialize_NullableId_WithNull_ProducesNull()
    {
        var nullJson = "null";

        var id = JsonSerializer.Deserialize<Id<User>?>(nullJson, _serializerOptions);

        id.Should().BeNull();
    }

    [Test]
    public void RoundTrip_NullableId_WithValue_PreservesValue()
    {
        var originalId = Id<User>.New();
        Id<User>? id = originalId;

        var json = JsonSerializer.Serialize(id, _serializerOptions);
        var deserialized = JsonSerializer.Deserialize<Id<User>?>(json, _serializerOptions);

        deserialized.Should().NotBeNull();
        deserialized!.Value.Should().Be(originalId);
    }

    [Test]
    public void RoundTrip_NullableId_WithNull_PreservesNull()
    {
        Id<User>? id = null;

        var json = JsonSerializer.Serialize(id, _serializerOptions);
        var deserialized = JsonSerializer.Deserialize<Id<User>?>(json, _serializerOptions);

        deserialized.Should().BeNull();
    }

    [Test]
    public void RoundTrip_ObjectWithNullableId_PreservesNull()
    {
        var original = new UserWithOptionalIdDto { Id = null };

        var json = JsonSerializer.Serialize(original, _serializerOptions);
        var deserialized = JsonSerializer.Deserialize<UserWithOptionalIdDto>(json, _serializerOptions);

        deserialized!.Id.Should().BeNull();
    }

    [Test]
    public void RoundTrip_ObjectWithNullableId_PreservesValue()
    {
        var userId = Id<User>.New();
        var original = new UserWithOptionalIdDto { Id = userId };

        var json = JsonSerializer.Serialize(original, _serializerOptions);
        var deserialized = JsonSerializer.Deserialize<UserWithOptionalIdDto>(json, _serializerOptions);

        deserialized!.Id.Should().NotBeNull();
        deserialized.Id!.Value.Should().Be(userId);
    }

    #endregion

    #region Shared Serialization Options

    [Test]
    public void SharedSerializationOptions_Supports_TypedIdSerialization()
    {
        var id = Id<User>.New();

        var json = JsonSerializer.Serialize(id, SharedSerializationOptions.Current);
        var deserialized = JsonSerializer.Deserialize<Id<User>>(json, SharedSerializationOptions.Current);

        deserialized.Should().Be(id);
    }

    [Test]
    public void SharedSerializationOptions_Supports_NullableTypedId()
    {
        Id<User>? id = Id<User>.New();

        var json = JsonSerializer.Serialize(id, SharedSerializationOptions.Current);
        var deserialized = JsonSerializer.Deserialize<Id<User>?>(json, SharedSerializationOptions.Current);

        deserialized.Should().NotBeNull();
        deserialized!.Value.Should().Be(id!.Value);
    }

    #endregion

    #region HTTP Model Binding (Request/Response Serialization)

    [Test]
    public void ModelBinding_DeserializesFromRequestJson_WithTypedId()
    {
        // Simulates incoming HTTP request body with typed ID
        var requestJson = """{"userId":"00000000-0000-0000-0000-000000000050","planId":"00000000-0000-0000-0000-000000000051"}""";

        var deserialized = JsonSerializer.Deserialize<CreatePlanRequest>(requestJson, _serializerOptions);

        deserialized.Should().NotBeNull();
        deserialized!.UserId.ToString().Should().Be("00000000-0000-0000-0000-000000000050");
        deserialized.PlanId.ToString().Should().Be("00000000-0000-0000-0000-000000000051");
    }

    [Test]
    public void ModelBinding_SerializesForResponseJson_WithTypedId()
    {
        // Simulates outgoing HTTP response body with typed ID
        var id = ParseTestId<User>("00000000-0000-0000-0000-000000000052");
        var response = new UserResponse { Id = id };

        var json = JsonSerializer.Serialize(response, _serializerOptions);

        json.Should().Contain("\"00000000-0000-0000-0000-000000000052\"");
    }

    [Test]
    public void ModelBinding_HandlesCamelCaseNaming_ForTypedIdFields()
    {
        // Verifies camel case naming policy works with typed IDs
        var options = new JsonSerializerOptions
        {
            PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
            Converters = { new TypedIdJsonConverterFactory() }
        };

        var request = new CreatePlanRequest
        {
            UserId = Id<User>.New(),
            PlanId = Id<Plan>.New()
        };

        var json = JsonSerializer.Serialize(request, options);
        var parsed = JsonSerializer.Deserialize<CreatePlanRequest>(json, options);

        parsed!.UserId.Should().Be(request.UserId);
        parsed.PlanId.Should().Be(request.PlanId);
    }

    #endregion

    #region Test Fixtures

    private class UserDto
    {
        public Id<User> Id { get; set; }
    }

    private class UserWithOptionalIdDto
    {
        public Id<User>? Id { get; set; }
    }

    private class UserResponse
    {
        public Id<User> Id { get; set; }
    }

    private class CreatePlanRequest
    {
        public Id<User> UserId { get; set; }
        public Id<Plan> PlanId { get; set; }
    }

    #endregion
}

