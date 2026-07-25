using LgymApi.Application.BuildingBlocks.Errors;
using LgymApi.Application.BuildingBlocks.Results;
using LgymApi.Application.Services;
using LgymApi.Domain.Entities;

namespace LgymApi.Application.ExternalAuth;

public interface IGoogleUserRegistrar
{
    Task<Result<User, AppError>> RegisterAsync(GoogleTokenPayload payload, CancellationToken cancellationToken);
}
