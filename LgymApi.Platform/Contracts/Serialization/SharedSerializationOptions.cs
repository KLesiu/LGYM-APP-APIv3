using System.Text.Json;
using System.Text.Json.Serialization;

namespace LgymApi.Application.Platform.Contracts.Serialization;

/// <summary>
/// Canonical shared serialization options for cross-module contract payloads.
/// Ensures consistent JSON serialization/deserialization across:
/// - Command payload persistence and dispatch (CommandDispatcher, BackgroundActionOrchestratorService)
/// - Email notification payload scheduling (EmailSchedulerService)
/// - Test helpers and compatibility verification
///
/// Options include backward-compatible read behavior for persisted payloads.
/// </summary>
public static class SharedSerializationOptions
{
    /// <summary>
    /// Gets the canonical JsonSerializerOptions for shared payloads.
    /// </summary>
    public static JsonSerializerOptions Current { get; } = CreateOptions();

    private static JsonSerializerOptions CreateOptions()
    {
        var options = new JsonSerializerOptions
        {
            PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
            DictionaryKeyPolicy = JsonNamingPolicy.CamelCase,
            DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
            // Allow reading properties with different casing (backward compatibility)
            PropertyNameCaseInsensitive = true
        };

        // Typed ID converter factory for Id<TEntity> serialization/deserialization
        options.Converters.Add(new TypedIdJsonConverterFactory());

        // String enums with integer fallback for backward compatibility with persisted payloads
        // Legacy payloads may contain integer enum values; we support both string names and integers on read
        options.Converters.Add(new JsonStringEnumConverter(namingPolicy: null, allowIntegerValues: true));

        return options;
    }
}
