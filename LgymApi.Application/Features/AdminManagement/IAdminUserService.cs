using LgymApi.Application.BuildingBlocks.Errors;
using LgymApi.Application.BuildingBlocks.Results;
using LgymApi.Application.Features.AdminManagement.Models;
using LgymApi.Application.Pagination;
using LgymApi.Domain.ValueObjects;

namespace LgymApi.Application.Features.AdminManagement;

public interface IAdminUserService
{
    Task<Result<Pagination<UserResult>, AppError>> GetUsersAsync(FilterInput filterInput, bool includeDeleted, CancellationToken cancellationToken = default);
    Task<Result<UserResult, AppError>> GetUserAsync(Id<global::LgymApi.Domain.Entities.User> userId, CancellationToken cancellationToken = default);
    Task<Result<Unit, AppError>> UpdateUserAsync(Id<global::LgymApi.Domain.Entities.User> targetUserId, Id<global::LgymApi.Domain.Entities.User> adminUserId, UpdateUserCommand command, CancellationToken cancellationToken = default);
    Task<Result<Unit, AppError>> DeleteUserAsync(Id<global::LgymApi.Domain.Entities.User> targetUserId, Id<global::LgymApi.Domain.Entities.User> adminUserId, CancellationToken cancellationToken = default);
    Task<Result<Unit, AppError>> BlockUserAsync(Id<global::LgymApi.Domain.Entities.User> targetUserId, Id<global::LgymApi.Domain.Entities.User> adminUserId, CancellationToken cancellationToken = default);
    Task<Result<Unit, AppError>> UnblockUserAsync(Id<global::LgymApi.Domain.Entities.User> targetUserId, CancellationToken cancellationToken = default);
}
