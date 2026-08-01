using LgymApi.Application.BuildingBlocks.Errors;
using LgymApi.Application.BuildingBlocks.Results;
using LgymApi.Domain.Entities;
using LgymApi.Domain.ValueObjects;

namespace LgymApi.Application.ExternalAuth;

public interface IAccountLinkingService
{
    Task<Result<Unit, AppError>> LinkGoogleAsync(Id<User> userId, string idToken, string? accessToken, CancellationToken cancellationToken);
    Task<Result<Unit, AppError>> UnlinkGoogleAsync(Id<User> userId, CancellationToken cancellationToken);
    Task<Result<IReadOnlyList<ExternalLoginInfo>, AppError>> GetExternalLoginsAsync(Id<User> userId, CancellationToken cancellationToken);
}