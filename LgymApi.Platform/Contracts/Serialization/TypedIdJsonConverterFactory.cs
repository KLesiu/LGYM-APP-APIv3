using System.Text.Json;
using System.Text.Json.Serialization;
using LgymApi.Domain.ValueObjects;

namespace LgymApi.Application.Platform.Contracts.Serialization;

/// <summary>
/// Factory for creating typed ID JSON converters dynamically.
/// Enables registration of Id{TEntity} converters in JsonSerializerOptions.
/// </summary>
public sealed class TypedIdJsonConverterFactory : JsonConverterFactory
{
    /// <summary>
    /// Determines whether this factory can convert the specified type.
    /// Returns true for any Id{TEntity} type.
    /// </summary>
    public override bool CanConvert(Type typeToConvert)
    {
        if (!typeToConvert.IsGenericType)
        {
            return false;
        }

        var genericDefinition = typeToConvert.GetGenericTypeDefinition();
        return genericDefinition == typeof(Id<>);
    }

    /// <summary>
    /// Creates a converter instance for the specified Id{TEntity} type.
    /// </summary>
    public override JsonConverter CreateConverter(Type typeToConvert, JsonSerializerOptions options)
    {
        if (!typeToConvert.IsGenericType)
        {
            throw new ArgumentException($"Type {typeToConvert} is not a generic type.", nameof(typeToConvert));
        }

        var entityType = typeToConvert.GetGenericArguments()[0];
        var converterType = typeof(TypedIdJsonConverter<>).MakeGenericType(entityType);

        return (JsonConverter)Activator.CreateInstance(converterType)!;
    }
}
