using LgymApi.Application.BuildingBlocks.Errors;
using LgymApi.Application.BuildingBlocks.Results;
using LgymApi.Domain.ValueObjects;
using UserEntity = LgymApi.Domain.Entities.User;

namespace LgymApi.Application.Identity.Contracts.Administration;

public interface IUserRoleAdministrationService
{
    Task<Result<Unit, AppError>> UpdateUserRolesAsync(
        Id<UserEntity> targetUserId,
        IReadOnlyCollection<string> roles,
        CancellationToken cancellationToken = default);
}
