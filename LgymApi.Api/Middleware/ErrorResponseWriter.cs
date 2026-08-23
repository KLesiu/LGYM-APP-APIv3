using System.Text.Json;
using System.Text.Json.Serialization;
using LgymApi.Api.Features.Common.Contracts;
using Microsoft.AspNetCore.Http.Json;
using Microsoft.Extensions.Options;

namespace LgymApi.Api.Middleware;

public static class ErrorResponseWriter
{
    public static async Task WriteAsync(
        HttpContext context,
        int statusCode,
        string message,
        CancellationToken cancellationToken = default,
        string? code = null)
    {
        if (context.Response.HasStarted)
        {
            var loggerFactory = context.RequestServices?.GetService<ILoggerFactory>();
            var logger = loggerFactory?.CreateLogger(nameof(ErrorResponseWriter));
            logger?.LogWarning("Attempted to write error response after response has started.");
            return;
        }

        context.Response.StatusCode = statusCode;

        // Try to get configured JsonOptions from DI; fall back to default if not available (e.g., in unit tests)
        var jsonOptions = context.RequestServices
            ?.GetService<IOptions<JsonOptions>>()
            ?.Value.SerializerOptions
            ?? CreateDefaultOptions();

        object payload = code is null
            ? new ResponseMessageDto { Message = message }
            : new LgymApi.Api.AgeGate.AgeGateErrorResponseDto { Message = message, Code = code };

        await context.Response.WriteAsJsonAsync(payload, jsonOptions, cancellationToken);
    }

    private static JsonSerializerOptions CreateDefaultOptions()
    {
        var options = new JsonSerializerOptions(JsonSerializerDefaults.Web)
        {
            DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
            PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        };
        return options;
    }
}
