using LgymApi.Application.Services;

namespace LgymApi.UnitTests.Fakes;

internal sealed class ConfigurableGoogleTokenValidator : IGoogleTokenValidator
{
    public List<(string IdToken, string? AccessToken, CancellationToken CancellationToken)> Calls { get; } = [];
    public Func<string, string?, CancellationToken, Task<GoogleTokenPayload?>> Validate { get; set; } = (_, _, _) => Task.FromResult<GoogleTokenPayload?>(null);

    public Task<GoogleTokenPayload?> ValidateAsync(string idToken, string? accessToken, CancellationToken ct)
    {
        Calls.Add((idToken, accessToken, ct));
        return Validate(idToken, accessToken, ct);
    }
}
