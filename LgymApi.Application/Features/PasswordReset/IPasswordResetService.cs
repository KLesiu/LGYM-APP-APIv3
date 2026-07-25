using LgymApi.Application.BuildingBlocks.Errors;
using LgymApi.Application.BuildingBlocks.Results;

using Result = LgymApi.Application.BuildingBlocks.Results.Result<LgymApi.Application.BuildingBlocks.Results.Unit, LgymApi.Application.BuildingBlocks.Errors.AppError>;

namespace LgymApi.Application.Features.PasswordReset;

public interface IPasswordResetService
{
    Task<Result> RequestPasswordResetAsync(string email, string cultureName, CancellationToken cancellationToken);
    Task<Result> ResetPasswordAsync(string plainTextToken, string newPassword, CancellationToken cancellationToken);
}
