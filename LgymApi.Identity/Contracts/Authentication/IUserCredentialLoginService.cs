using LgymApi.Application.BuildingBlocks.Errors;
using LgymApi.Application.BuildingBlocks.Results;
using LgymApi.Application.Features.User.Models;

namespace LgymApi.Application.Identity.Contracts.Authentication;

public interface IUserCredentialLoginService
{
    Task<Result<LoginResult, AppError>> LoginAsync(string name, string password, CancellationToken cancellationToken = default);
    Task<Result<LoginResult, AppError>> LoginTrainerAsync(string name, string password, CancellationToken cancellationToken = default);
}
