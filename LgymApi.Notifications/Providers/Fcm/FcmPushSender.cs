using System.Net;
using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;
using LgymApi.Application.Notifications.Contracts.Push;
using LgymApi.Application.Notifications.Repositories;
using LgymApi.Application.Platform.Contracts.Serialization;
using LgymApi.Domain.Entities;
using LgymApi.Domain.ValueObjects;
using Google.Apis.Auth.OAuth2;
using Microsoft.Extensions.Logging;

namespace LgymApi.Application.Notifications.Providers.Fcm;

internal sealed class FcmPushSender : IPushProviderSender
{
    private const string MessagingScope = "https://www.googleapis.com/auth/firebase.messaging";
    private const string NotificationTitle = "LGYM";
    private const string NotificationBody = "You have a new notification.";
    private const string AcceptedResponseSummary = "accepted";
    private const string ReceivedResponseSummary = "provider-response-received";

    private static readonly HashSet<string> KnownProviderStatuses = new(StringComparer.Ordinal)
    {
        "CANCELLED",
        "DEADLINE_EXCEEDED",
        "INTERNAL",
        "INVALID_ARGUMENT",
        "NOT_FOUND",
        "PERMISSION_DENIED",
        "QUOTA_EXCEEDED",
        "RESOURCE_EXHAUSTED",
        "SENDER_ID_MISMATCH",
        "THIRD_PARTY_AUTH_ERROR",
        "UNAUTHENTICATED",
        "UNAVAILABLE",
        "UNREGISTERED"
    };

    private readonly IHttpClientFactory _httpClientFactory;
    private readonly IPushInstallationRepository _pushInstallationRepository;
    private readonly PushNotificationOptions _options;
    private readonly ILogger<FcmPushSender> _logger;

    public FcmPushSender(
        IHttpClientFactory httpClientFactory,
        IPushInstallationRepository pushInstallationRepository,
        PushNotificationOptions options,
        ILogger<FcmPushSender> logger)
    {
        _httpClientFactory = httpClientFactory;
        _pushInstallationRepository = pushInstallationRepository;
        _options = options;
        _logger = logger;
    }

    public async Task<PushSendAttemptResult> SendAsync(
        Id<PushInstallation> installationId,
        PushEventPayload payload,
        CancellationToken cancellationToken = default)
    {
        if (!_options.IsSendEnabled)
        {
            return new PushSendAttemptResult(
                PushSendOutcome.Skipped,
                "Skipped",
                null,
                "push-disabled",
                "Push notifications are disabled.");
        }

        try
        {
            var installation = await _pushInstallationRepository.FindByIdAsync(installationId, cancellationToken)
                ?? throw new InvalidOperationException("Push installation was not found for provider delivery.");

            var accessToken = await GetAccessTokenAsync(cancellationToken);
            using var request = new HttpRequestMessage(HttpMethod.Post, BuildSendUrl())
            {
                Content = BuildRequestContent(installation.FcmToken, payload)
            };
            request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", accessToken);

            var client = _httpClientFactory.CreateClient(nameof(FcmPushSender));
            using var response = await client.SendAsync(request, cancellationToken);
            var body = await response.Content.ReadAsStringAsync(cancellationToken);

            if (response.IsSuccessStatusCode)
            {
                var providerMessageId = TryExtractProviderMessageId(body);
                return new PushSendAttemptResult(
                    PushSendOutcome.Sent,
                    response.StatusCode.ToString(),
                    providerMessageId,
                    null,
                    AcceptedResponseSummary);
            }

            var summary = Summarize(body);
            var errorCode = TryExtractProviderStatus(body) ?? response.StatusCode.ToString();
            var outcome = ClassifyFailure(response.StatusCode, body);
            _logger.LogWarning(
                "FCM send failed for installation {InstallationId}, event {EventId}, category {Category} with provider status {ProviderStatus}, outcome {Outcome}, and summary {ProviderSummary}.",
                installation.InstallationId,
                payload.EventId,
                payload.Type,
                response.StatusCode,
                outcome,
                summary);

            return new PushSendAttemptResult(
                outcome,
                response.StatusCode.ToString(),
                null,
                errorCode,
                summary);
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (Exception)
        {
            throw new InvalidOperationException("FCM provider delivery failed.");
        }
    }

    private async Task<string> GetAccessTokenAsync(CancellationToken cancellationToken)
    {
        GoogleCredential credential;
        if (!string.IsNullOrWhiteSpace(_options.Fcm.CredentialsJson))
        {
            credential = CreateCredentialFromJson(_options.Fcm.CredentialsJson);
        }
        else if (!string.IsNullOrWhiteSpace(_options.Fcm.CredentialsPath))
        {
            credential = CreateCredentialFromFile(_options.Fcm.CredentialsPath);
        }
        else
        {
            throw new InvalidOperationException("FCM credentials are not configured.");
        }

        credential = credential.CreateScoped(MessagingScope);
        return await credential.UnderlyingCredential.GetAccessTokenForRequestAsync(null, cancellationToken);
    }

    private static GoogleCredential CreateCredentialFromJson(string credentialsJson)
    {
        using var stream = new MemoryStream(Encoding.UTF8.GetBytes(credentialsJson));
        return CredentialFactory.FromStream<ServiceAccountCredential>(stream).ToGoogleCredential();
    }

    private static GoogleCredential CreateCredentialFromFile(string credentialsPath)
    {
        using var stream = File.OpenRead(credentialsPath);
        return CredentialFactory.FromStream<ServiceAccountCredential>(stream).ToGoogleCredential();
    }

    private StringContent BuildRequestContent(string deviceToken, PushEventPayload payload)
    {
        var request = new
        {
            message = new
            {
                token = deviceToken,
                notification = new
                {
                    title = NotificationTitle,
                    body = NotificationBody
                },
                android = new
                {
                    priority = "HIGH"
                },
                data = new Dictionary<string, string>
                {
                    ["schemaVersion"] = payload.SchemaVersion.ToString(),
                    ["type"] = payload.Type,
                    ["eventId"] = payload.EventId,
                    ["entityId"] = payload.EntityId ?? string.Empty,
                    ["inAppNotificationId"] = payload.InAppNotificationId?.ToString() ?? string.Empty,
                    ["deeplink"] = payload.Deeplink ?? string.Empty
                }
            }
        };

        var json = JsonSerializer.Serialize(request, SharedSerializationOptions.Current);
        return new StringContent(json, Encoding.UTF8, "application/json");
    }

    private string BuildSendUrl()
    {
        return $"{_options.Fcm.BaseUrl}/v1/projects/{_options.Fcm.ProjectId}/messages:send";
    }

    private static PushSendOutcome ClassifyFailure(HttpStatusCode statusCode, string providerResponse)
    {
        if (ContainsAny(providerResponse, "unregistered", "registration-token-not-registered", "invalid registration token", "not a valid fcm registration token"))
        {
            return PushSendOutcome.InvalidToken;
        }

        if (statusCode == HttpStatusCode.TooManyRequests
            || statusCode == HttpStatusCode.RequestTimeout
            || statusCode == HttpStatusCode.BadGateway
            || statusCode == HttpStatusCode.ServiceUnavailable
            || statusCode == HttpStatusCode.GatewayTimeout
            || (int)statusCode >= 500)
        {
            return PushSendOutcome.TransientFailure;
        }

        if (ContainsAny(providerResponse, "unavailable", "internal", "timeout", "temporar"))
        {
            return PushSendOutcome.TransientFailure;
        }

        return PushSendOutcome.PermanentFailure;
    }

    private static string? TryExtractValue(string body, string propertyName)
    {
        if (string.IsNullOrWhiteSpace(body))
        {
            return null;
        }

        try
        {
            using var document = JsonDocument.Parse(body);
            if (document.RootElement.ValueKind == JsonValueKind.Object)
            {
                if (document.RootElement.TryGetProperty(propertyName, out var directValue) && directValue.ValueKind == JsonValueKind.String)
                {
                    return directValue.GetString();
                }

                if (document.RootElement.TryGetProperty("error", out var error)
                    && error.ValueKind == JsonValueKind.Object
                    && error.TryGetProperty(propertyName, out var nestedValue)
                    && nestedValue.ValueKind == JsonValueKind.String)
                {
                    return nestedValue.GetString();
                }
            }
        }
        catch (JsonException)
        {
            return null;
        }

        return null;
    }

    private static bool ContainsAny(string value, params string[] terms)
    {
        return terms.Any(term => value.Contains(term, StringComparison.OrdinalIgnoreCase));
    }

    private static string Summarize(string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return string.Empty;
        }

        return TryExtractProviderStatus(value) is { } status
            ? $"status={status}"
            : ReceivedResponseSummary;
    }

    private static string? TryExtractProviderStatus(string body)
    {
        var status = TryExtractValue(body, "status");
        return status != null && KnownProviderStatuses.Contains(status)
            ? status
            : null;
    }

    private static string? TryExtractProviderMessageId(string body)
    {
        var messageId = TryExtractValue(body, "name");
        if (string.IsNullOrWhiteSpace(messageId)
            || messageId.Length > 200
            || !messageId.StartsWith("projects/", StringComparison.Ordinal)
            || !messageId.Contains("/messages/", StringComparison.Ordinal)
            || messageId.Any(character => !char.IsAsciiLetterOrDigit(character) && character is not '/' and not '-' and not '_' and not '.' and not ':'))
        {
            return null;
        }

        return messageId;
    }
}
