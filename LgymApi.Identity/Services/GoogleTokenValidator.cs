using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json.Serialization;
using Google.Apis.Auth;
using LgymApi.Application.Services;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;

namespace LgymApi.Infrastructure.Services;

internal sealed class GoogleTokenValidator : IGoogleTokenValidator
{
    // Google userinfo endpoint is used as a fallback when a valid ID token does not contain email/profile claims.
    private const string GoogleUserInfoUrl = "https://openidconnect.googleapis.com/v1/userinfo";

    private readonly IConfiguration _configuration;
    private readonly IHttpClientFactory _httpClientFactory;
    private readonly ILogger<GoogleTokenValidator> _logger;

    public GoogleTokenValidator(
        IConfiguration configuration,
        IHttpClientFactory httpClientFactory,
        ILogger<GoogleTokenValidator> logger)
    {
        _configuration = configuration;
        _httpClientFactory = httpClientFactory;
        _logger = logger;
    }

    public async Task<GoogleTokenPayload?> ValidateAsync(string idToken, string? accessToken, CancellationToken ct)
    {
        ct.ThrowIfCancellationRequested();

        var clientId = _configuration["GoogleAuth:ClientId"];
        if (string.IsNullOrWhiteSpace(clientId))
        {
            throw new InvalidOperationException("GoogleAuth:ClientId configuration is missing.");
        }

        try
        {
            var payload = await GoogleJsonWebSignature.ValidateAsync(
                idToken,
                new GoogleJsonWebSignature.ValidationSettings
                {
                    Audience = [clientId]
                });

            // We always require Google subject because it is the stable external identifier we persist in ProviderKey.
            if (string.IsNullOrWhiteSpace(payload.Subject))
            {
                return null;
            }

            // Some Google flows return a valid ID token without email/profile claims, so we enrich them from userinfo using access_token.
            var enrichedUserInfo = await ResolveUserInfoAsync(payload, accessToken, ct);

            // We still require email because the current registration/linking flow persists and deduplicates users by email.
            if (string.IsNullOrWhiteSpace(enrichedUserInfo.Email))
            {
                return null;
            }

            return new GoogleTokenPayload(
                payload.Subject,
                enrichedUserInfo.Email,
                enrichedUserInfo.EmailVerified,
                enrichedUserInfo.Name,
                enrichedUserInfo.Picture);
        }
        catch (InvalidJwtException ex)
        {
            _logger.LogWarning(ex, "Google token validation failed.");
            return null;
        }
    }

    // This fallback keeps Google sign-in working when Google omits email/profile claims from the ID token.
    private async Task<ResolvedGoogleUserInfo> ResolveUserInfoAsync(
        GoogleJsonWebSignature.Payload payload,
        string? accessToken,
        CancellationToken ct)
    {
        if (!string.IsNullOrWhiteSpace(payload.Email))
        {
            return new ResolvedGoogleUserInfo(
                payload.Email,
                payload.EmailVerified,
                payload.Name,
                payload.Picture);
        }

        // Without access_token there is no trustworthy fallback source for the missing email claim.
        if (string.IsNullOrWhiteSpace(accessToken))
        {
            return ResolvedGoogleUserInfo.Empty;
        }

        try
        {
            var client = _httpClientFactory.CreateClient();
            using var request = new HttpRequestMessage(HttpMethod.Get, GoogleUserInfoUrl);
            request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", accessToken);

            using var response = await client.SendAsync(request, ct);
            if (!response.IsSuccessStatusCode)
            {
                _logger.LogWarning("Google userinfo request failed with status code {StatusCode}.", response.StatusCode);
                return ResolvedGoogleUserInfo.Empty;
            }

            var userInfo = await response.Content.ReadFromJsonAsync<GoogleUserInfoResponse>(cancellationToken: ct);
            if (userInfo == null)
            {
                return ResolvedGoogleUserInfo.Empty;
            }

            // We only trust userinfo when it belongs to the same Google account as the validated ID token.
            if (!string.Equals(userInfo.Sub, payload.Subject, StringComparison.Ordinal))
            {
                _logger.LogWarning("Google userinfo subject did not match the validated ID token subject.");
                return ResolvedGoogleUserInfo.Empty;
            }

            return new ResolvedGoogleUserInfo(
                userInfo.Email?.Trim(),
                userInfo.EmailVerified ?? payload.EmailVerified,
                userInfo.Name ?? payload.Name,
                userInfo.Picture ?? payload.Picture);
        }
        catch (Exception ex)
        {
            // Userinfo fallback should never crash auth; if Google profile lookup fails we simply treat the token as incomplete.
            _logger.LogWarning(ex, "Google userinfo fallback failed.");
            return ResolvedGoogleUserInfo.Empty;
        }
    }

    // Dedicated DTO for the Google userinfo fallback response.
    private sealed record GoogleUserInfoResponse
    {
        [JsonPropertyName("sub")]
        public string? Sub { get; init; }

        [JsonPropertyName("email")]
        public string? Email { get; init; }

        [JsonPropertyName("email_verified")]
        public bool? EmailVerified { get; init; }

        [JsonPropertyName("name")]
        public string? Name { get; init; }

        [JsonPropertyName("picture")]
        public string? Picture { get; init; }
    }

    // Internal normalized shape so the rest of the validator always works with a single resolved user-info object.
    private sealed record ResolvedGoogleUserInfo(string? Email, bool EmailVerified, string? Name, string? Picture)
    {
        public static readonly ResolvedGoogleUserInfo Empty = new(null, false, null, null);
    }
}
