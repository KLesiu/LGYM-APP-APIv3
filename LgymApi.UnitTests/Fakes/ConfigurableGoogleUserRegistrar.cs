using LgymApi.Application.BuildingBlocks.Errors;
using LgymApi.Application.BuildingBlocks.Results;
using LgymApi.Application.ExternalAuth;
using LgymApi.Application.Services;
using LgymApi.Domain.Entities;

namespace LgymApi.UnitTests.Fakes;

internal sealed class ConfigurableGoogleUserRegistrar : IGoogleUserRegistrar
{
    public List<(GoogleTokenPayload Payload, CancellationToken CancellationToken)> Calls { get; } = [];
    public Func<GoogleTokenPayload, CancellationToken, Task<Result<User, AppError>>> Register { get; set; } = (_, _) => throw new NotSupportedException("Google registrar was not configured.");

    public Task<Result<User, AppError>> RegisterAsync(GoogleTokenPayload payload, CancellationToken cancellationToken)
    {
        Calls.Add((payload, cancellationToken));
        return Register(payload, cancellationToken);
    }
}
