using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using LgymApi.Api.Idempotency;
using LgymApi.Application.Repositories;
using LgymApi.Domain.Entities;
using LgymApi.Domain.ValueObjects;
using Microsoft.AspNetCore.Http.Extensions;

namespace LgymApi.Api.Middleware;

/// <summary>
/// Middleware for API-layer idempotency: validates Idempotency-Key header, handles replay/conflict detection,
/// and persists response snapshots for replay-safe duplicate request handling.
/// </summary>
public sealed class ApiIdempotencyMiddleware
{
    private readonly RequestDelegate _next;

    public ApiIdempotencyMiddleware(RequestDelegate next)
    {
        _next = next;
    }

    public async Task InvokeAsync(
        HttpContext context,
        IApiIdempotencyRecordRepository idempotencyRepository,
        IUnitOfWork unitOfWork)
    {
        var endpoint = context.GetEndpoint();
        var idempotencyMetadata = endpoint?.Metadata.GetMetadata<ApiIdempotencyAttribute>();

        // If endpoint is not marked with [ApiIdempotency], bypass middleware
        if (idempotencyMetadata == null)
        {
            await _next(context);
            return;
        }

        // Extract and validate idempotency key
        if (!context.Request.Headers.TryGetValue(ApiIdempotencyHeaders.IdempotencyKey, out var keyHeader))
        {
            context.Response.StatusCode = StatusCodes.Status400BadRequest;
            await context.Response.WriteAsJsonAsync(new
            {
                message = "Idempotency key is required.",
                code = "IDEMPOTENCY_KEY_REQUIRED"
            });
            return;
        }

        var idempotencyKey = keyHeader.ToString().Trim();
        if (string.IsNullOrWhiteSpace(idempotencyKey))
        {
            context.Response.StatusCode = StatusCodes.Status400BadRequest;
            await context.Response.WriteAsJsonAsync(new
            {
                message = "Idempotency key is required.",
                code = "IDEMPOTENCY_KEY_REQUIRED"
            });
            return;
        }

        // Build scope tuple
        var scopeTuple = await BuildScopeTupleAsync(context, idempotencyMetadata);

        // Compute request fingerprint
        var requestFingerprint = await ComputeFingerprintAsync(context, scopeTuple);

        // Check for existing idempotency record
        var existingRecord = await idempotencyRepository.FindByScopeAndKeyAsync(
            scopeTuple,
            idempotencyKey,
            context.RequestAborted);

        ApiIdempotencyRecord? record = null;

        if (existingRecord != null)
        {
            // If response not yet captured (in-progress), do not replay - let request proceed normally
            // This handles crash-window scenario where record was persisted but response not yet captured
            if (existingRecord.ResponseStatusCode < 100)
            {
                // Record exists but is in-progress - this is a concurrent request or crash-window recovery
                // Update our local reference to the existing record and continue processing
                record = existingRecord;
            }
            else
            {
                // Same fingerprint = replay completed response
                if (existingRecord.RequestFingerprint == requestFingerprint)
                {
                    context.Response.StatusCode = existingRecord.ResponseStatusCode;
                    context.Response.ContentType = "application/json";
                    await context.Response.WriteAsync(existingRecord.ResponseBodyJson, context.RequestAborted);
                    return;
                }

                // Different fingerprint = conflict
                context.Response.StatusCode = StatusCodes.Status409Conflict;
                await context.Response.WriteAsJsonAsync(new
                {
                    message = "Idempotency key reused with different request payload.",
                    code = "IDEMPOTENCY_KEY_FINGERPRINT_MISMATCH"
                });
                return;
            }
        }

        // Create in-progress idempotency record (if not already exists from concurrent request)
        if (record == null)
        {
            record = new ApiIdempotencyRecord
            {
                Id = Id<ApiIdempotencyRecord>.New(),
                IdempotencyKey = idempotencyKey,
                ScopeTuple = scopeTuple,
                RequestFingerprint = requestFingerprint,
                ResponseStatusCode = 0,
                ResponseBodyJson = string.Empty,
                ProcessedAt = DateTimeOffset.UtcNow
            };

            // Persist idempotency record (capture return value in case existing record found during race)
            record = await idempotencyRepository.AddOrGetExistingAsync(record, context.RequestAborted);
            await unitOfWork.SaveChangesAsync(context.RequestAborted);
        }

        // Capture response
        var originalBodyStream = context.Response.Body;
        using var responseBodyStream = new MemoryStream();
        context.Response.Body = responseBodyStream;

        try
        {
            await _next(context);

            // Read response body
            responseBodyStream.Seek(0, SeekOrigin.Begin);
            var responseBody = await new StreamReader(responseBodyStream).ReadToEndAsync(context.RequestAborted);

            // Update idempotency record with response snapshot
            record.ResponseStatusCode = context.Response.StatusCode;
            record.ResponseBodyJson = responseBody;
            await idempotencyRepository.UpdateAsync(record, context.RequestAborted);
            await unitOfWork.SaveChangesAsync(context.RequestAborted);

            // Copy response back to original stream
            responseBodyStream.Seek(0, SeekOrigin.Begin);
            await responseBodyStream.CopyToAsync(originalBodyStream, context.RequestAborted);
        }
        finally
        {
            context.Response.Body = originalBodyStream;
        }
    }

    private static async Task<string> BuildScopeTupleAsync(
        HttpContext context,
        ApiIdempotencyAttribute metadata)
    {
        var method = context.Request.Method;
        var routeTemplate = metadata.RouteTemplate;

        string callerScope;
        if (metadata.ScopeSource == ApiIdempotencyScopeSource.AuthenticatedUser)
        {
            callerScope = context.GetCurrentAccountId().IsEmpty
                ? "anonymous"
                : context.GetCurrentAccountId().ToString();
        }
        else // NormalizedEmail
        {
            context.Request.EnableBuffering();
            context.Request.Body.Position = 0;
            using var reader = new StreamReader(context.Request.Body, leaveOpen: true);
            var bodyText = await reader.ReadToEndAsync();
            context.Request.Body.Position = 0;

            var bodyJson = JsonDocument.Parse(bodyText);
            var email = bodyJson.RootElement.GetProperty("email").GetString() ?? string.Empty;
            callerScope = email.Trim().ToLowerInvariant();
        }

        return $"{method}|{routeTemplate}|{callerScope}";
    }

    private static async Task<string> ComputeFingerprintAsync(HttpContext context, string scopeTuple)
    {
        context.Request.EnableBuffering();
        context.Request.Body.Position = 0;

        using var reader = new StreamReader(context.Request.Body, leaveOpen: true);
        var bodyText = await reader.ReadToEndAsync();
        context.Request.Body.Position = 0;

        // Canonicalize JSON body
        var canonicalBody = string.Empty;
        if (!string.IsNullOrWhiteSpace(bodyText))
        {
            try
            {
                var jsonDoc = JsonDocument.Parse(bodyText);
                canonicalBody = JsonSerializer.Serialize(jsonDoc, new JsonSerializerOptions
                {
                    WriteIndented = false
                });
            }
            catch
            {
                canonicalBody = bodyText;
            }
        }

        // Route params
        var routeParams = string.Join("&",
            context.Request.RouteValues
                .OrderBy(kv => kv.Key)
                .Select(kv => $"{kv.Key}={kv.Value}"));

        // Query string
        var query = context.Request.QueryString.HasValue
            ? context.Request.QueryString.Value
            : string.Empty;

        var fingerprintInput = $"{canonicalBody}|{routeParams}|{query}|{scopeTuple}";
        var hashBytes = SHA256.HashData(Encoding.UTF8.GetBytes(fingerprintInput));
        return Convert.ToHexString(hashBytes).ToLowerInvariant();
    }
}
