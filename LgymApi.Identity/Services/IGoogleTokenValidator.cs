namespace LgymApi.Application.Services;

internal interface IGoogleTokenValidator
{
    Task<GoogleTokenPayload?> ValidateAsync(string idToken, string? accessToken, CancellationToken ct);
}
