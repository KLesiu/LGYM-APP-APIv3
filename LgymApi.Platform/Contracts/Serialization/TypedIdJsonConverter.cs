using System.Text.Json;
using System.Text.Json.Serialization;
using LgymApi.Domain.ValueObjects;

namespace LgymApi.Application.Platform.Contracts.Serialization;

/// <summary>
/// JSON converter for typed entity IDs (Id{TEntity}).
/// Serializes as UUID string and deserializes from UUID string.
/// Supports nullable Id{TEntity}? through struct default behavior.
/// </summary>
/// <typeparam name="TEntity">The entity type this ID represents.</typeparam>
public sealed class TypedIdJsonConverter<TEntity> : JsonConverter<Id<TEntity>>
{
    /// <summary>
    /// Reads an Id{TEntity} from JSON as a string UUID.
    /// </summary>
    public override Id<TEntity> Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options)
    {
        if (reader.TokenType == JsonTokenType.Null)
        {
            throw new JsonException("Id<TEntity> cannot be null. Use nullable Id<TEntity>? for optional IDs.");
        }

        if (reader.TokenType != JsonTokenType.String)
        {
            throw new JsonException($"Expected string token for Id<TEntity>, got {reader.TokenType}.");
        }

        var guidString = reader.GetString();
        if (string.IsNullOrWhiteSpace(guidString))
        {
            throw new JsonException("Id<TEntity> string value cannot be empty.");
        }

        if (!Id<TEntity>.TryParse(guidString, out var id))
        {
            throw new JsonException($"Invalid GUID format for Id<TEntity>: '{guidString}'.");
        }

        return id;
    }

    /// <summary>
    /// Writes an Id{TEntity} to JSON as a string UUID.
    /// </summary>
    public override void Write(Utf8JsonWriter writer, Id<TEntity> value, JsonSerializerOptions options)
    {
        writer.WriteStringValue(value.ToString());
    }
}
