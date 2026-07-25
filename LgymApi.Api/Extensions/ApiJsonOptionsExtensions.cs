using System.Text.Json;
using System.Text.Json.Serialization;
using LgymApi.Application.Platform.Contracts.Serialization;
using Microsoft.AspNetCore.Http.Json;
using Microsoft.Extensions.DependencyInjection;

namespace LgymApi.Api.Extensions;

public static class ApiJsonOptionsExtensions
{
    public static IServiceCollection AddStrictHttpJsonOptions(this IServiceCollection services)
    {
        services
            .AddControllers()
            .AddJsonOptions(options => ConfigureJsonSerializerOptions(options.JsonSerializerOptions));
        services.Configure<JsonOptions>(options => ConfigureJsonSerializerOptions(options.SerializerOptions));

        return services;
    }

    private static void ConfigureJsonSerializerOptions(JsonSerializerOptions options)
    {
        options.PropertyNamingPolicy = JsonNamingPolicy.CamelCase;
        options.DictionaryKeyPolicy = JsonNamingPolicy.CamelCase;
        options.DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull;
        options.Converters.Add(new TypedIdJsonConverterFactory());
        options.Converters.Add(new JsonStringEnumConverter(namingPolicy: null, allowIntegerValues: false));
    }
}
