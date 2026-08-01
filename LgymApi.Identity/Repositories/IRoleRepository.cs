using LgymApi.Application.Pagination;
using LgymApi.Application.Pagination;
using LgymApi.Domain.Entities;
using LgymApi.Domain.ValueObjects;

namespace LgymApi.Application.Repositories;

internal interface IRoleRepository
{
    Task<List<Role>> GetAllAsync(CancellationToken cancellationToken = default);
    Task<Role?> FindByIdAsync(Id<Role> roleId, CancellationToken cancellationToken = default);
    Task<Role?> FindByNameAsync(string roleName, CancellationToken cancellationToken = default);
    Task<List<Role>> GetByNamesAsync(IReadOnlyCollection<string> roleNames, CancellationToken cancellationToken = default);
    Task<bool> ExistsByNameAsync(string roleName, Id<Role>? excludeRoleId = null, CancellationToken cancellationToken = default);
    Task<List<string>> GetRoleNamesByUserIdAsync(Id<User> userId, CancellationToken cancellationToken = default);
    Task<Dictionary<Id<User>, List<string>>> GetRoleNamesByUserIdsAsync(IReadOnlyCollection<Id<User>> userIds, CancellationToken cancellationToken = default);
    Task<List<string>> GetPermissionClaimsByUserIdAsync(Id<User> userId, CancellationToken cancellationToken = default);
    Task<List<string>> GetPermissionClaimsByRoleIdAsync(Id<Role> targetRoleId, CancellationToken cancellationToken = default);
    Task<Dictionary<Id<Role>, List<string>>> GetPermissionClaimsByRoleIdsAsync(IReadOnlyCollection<Id<Role>> targetRoleIds, CancellationToken cancellationToken = default);
    Task<bool> UserHasRoleAsync(Id<User> userId, string roleName, CancellationToken cancellationToken = default);
    Task<bool> UserHasPermissionAsync(Id<User> userId, string permission, CancellationToken cancellationToken = default);
    Task AddRoleAsync(Role role, CancellationToken cancellationToken = default);
    Task UpdateRoleAsync(Role role, CancellationToken cancellationToken = default);
    Task DeleteRoleAsync(Role role, CancellationToken cancellationToken = default);
    Task ReplaceRolePermissionClaimsAsync(Id<Role> targetRoleId, IReadOnlyCollection<string> permissionClaims, CancellationToken cancellationToken = default);
    Task AddUserRolesAsync(Id<User> userId, IReadOnlyCollection<Id<Role>> roleIds, CancellationToken cancellationToken = default);
    Task ReplaceUserRolesAsync(Id<User> userId, IReadOnlyCollection<Id<Role>> roleIds, CancellationToken cancellationToken = default);
    Task<Pagination<Role>> GetRolesPaginatedAsync(FilterInput filterInput, CancellationToken cancellationToken = default);
}
