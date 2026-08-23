using LgymApi.Application.BuildingBlocks.Errors;
using LgymApi.Application.BuildingBlocks.Results;
using LgymApi.Application.Features.User.Models;

namespace LgymApi.Application.ExternalAuth;

public interface IExternalAuthService
{
    Task<Result<LoginResult, AppError>> GoogleSignInAsync(string idToken, string? accessToken, CancellationToken cancellationToken);

    Task<Result<LoginResult, AppError>> GoogleSignInAsync(
        string idToken,
        string? accessToken,
        bool adultConfirmed,
        CancellationToken cancellationToken);
}
