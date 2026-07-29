using LgymApi.Application.BuildingBlocks.Errors;
using LgymApi.Application.BuildingBlocks.Results;
using LgymApi.Application.Features.User.Models;
using LgymApi.Domain.Entities;

namespace LgymApi.Application.ExternalAuth;

internal interface ILoginResultBuilder
{
    Task<Result<LoginResult, AppError>> BuildAsync(User user, string preferredTimeZone, CancellationToken cancellationToken);
}
