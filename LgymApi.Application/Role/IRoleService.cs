using LgymApi.Application.BuildingBlocks.Errors;
using LgymApi.Application.BuildingBlocks.Results;
using LgymApi.Application.Features.Role.Models;
using LgymApi.Application.Pagination;
using LgymApi.Domain.ValueObjects;

namespace LgymApi.Application.Features.Role;

public interface IRoleService
{
    Task<Result<List<RoleResult>, AppError>> GetRolesAsync(CancellationToken cancellationToken = default);
    Task<Result<Pagination<RoleResult>, AppError>> GetRolesPaginatedAsync(FilterInput filterInput, CancellationToken cancellationToken = default);
    Task<Result<RoleResult, AppError>> GetRoleAsync(Id<LgymApi.Domain.Entities.Role> roleId, CancellationToken cancellationToken = default);
    Task<Result<RoleResult, AppError>> CreateRoleAsync(string name, string? description, IReadOnlyCollection<string> permissionClaims, CancellationToken cancellationToken = default);
    Task<Result<Unit, AppError>> UpdateRoleAsync(Id<LgymApi.Domain.Entities.Role> roleId, string name, string? description, IReadOnlyCollection<string> permissionClaims, CancellationToken cancellationToken = default);
    Task<Result<Unit, AppError>> DeleteRoleAsync(Id<LgymApi.Domain.Entities.Role> roleId, CancellationToken cancellationToken = default);
    List<PermissionClaimLookupResult> GetAvailablePermissionClaims();
    Task<Result<Unit, AppError>> UpdateUserRolesAsync(Id<LgymApi.Domain.Entities.User> userId, IReadOnlyCollection<string> roleNames, CancellationToken cancellationToken = default);
}
