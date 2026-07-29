using LgymApi.Application.Pagination;
using LgymApi.Application.Repositories;
using LgymApi.Domain.Entities;
using LgymApi.Domain.ValueObjects;

namespace LgymApi.UnitTests.Fakes;

internal sealed class ConfigurableRoleRepository : IRoleRepository
{
    public List<(string Method, object? Argument, CancellationToken CancellationToken)> Calls { get; } = [];
    public Func<CancellationToken, Task<List<Role>>> GetAll { get; set; } = _ => Task.FromResult(new List<Role>());
    public Func<Id<Role>, CancellationToken, Task<Role?>> FindById { get; set; } = (_, _) => Task.FromResult<Role?>(null);
    public Func<string, CancellationToken, Task<Role?>> FindByName { get; set; } = (_, _) => Task.FromResult<Role?>(null);
    public Func<IReadOnlyCollection<string>, CancellationToken, Task<List<Role>>> GetByNames { get; set; } = (_, _) => Task.FromResult(new List<Role>());
    public Func<string, Id<Role>?, CancellationToken, Task<bool>> ExistsByName { get; set; } = (_, _, _) => Task.FromResult(false);
    public Func<Id<User>, CancellationToken, Task<List<string>>> GetRoleNamesByUserId { get; set; } = (_, _) => Task.FromResult(new List<string>());
    public Func<IReadOnlyCollection<Id<User>>, CancellationToken, Task<Dictionary<Id<User>, List<string>>>> GetRoleNamesByUserIds { get; set; } = (_, _) => Task.FromResult(new Dictionary<Id<User>, List<string>>());
    public Func<Id<User>, CancellationToken, Task<List<string>>> GetPermissionClaimsByUserId { get; set; } = (_, _) => Task.FromResult(new List<string>());
    public Func<Id<Role>, CancellationToken, Task<List<string>>> GetPermissionClaimsByRoleId { get; set; } = (_, _) => Task.FromResult(new List<string>());
    public Func<IReadOnlyCollection<Id<Role>>, CancellationToken, Task<Dictionary<Id<Role>, List<string>>>> GetPermissionClaimsByRoleIds { get; set; } = (_, _) => Task.FromResult(new Dictionary<Id<Role>, List<string>>());
    public Func<Id<User>, string, CancellationToken, Task<bool>> UserHasRole { get; set; } = (_, _, _) => Task.FromResult(false);
    public Func<Id<User>, string, CancellationToken, Task<bool>> UserHasPermission { get; set; } = (_, _, _) => Task.FromResult(false);
    public Func<Role, CancellationToken, Task> AddRole { get; set; } = (_, _) => Task.CompletedTask;
    public Func<Role, CancellationToken, Task> UpdateRole { get; set; } = (_, _) => Task.CompletedTask;
    public Func<Role, CancellationToken, Task> DeleteRole { get; set; } = (_, _) => Task.CompletedTask;
    public Func<Id<Role>, IReadOnlyCollection<string>, CancellationToken, Task> ReplaceRolePermissionClaims { get; set; } = (_, _, _) => Task.CompletedTask;
    public Func<Id<User>, IReadOnlyCollection<Id<Role>>, CancellationToken, Task> AddUserRoles { get; set; } = (_, _, _) => Task.CompletedTask;
    public Func<Id<User>, IReadOnlyCollection<Id<Role>>, CancellationToken, Task> ReplaceUserRoles { get; set; } = (_, _, _) => Task.CompletedTask;
    public Func<FilterInput, CancellationToken, Task<Pagination<Role>>> GetRolesPaginated { get; set; } = (_, _) => Task.FromResult(new Pagination<Role>());

    public Task<List<Role>> GetAllAsync(CancellationToken cancellationToken = default) { Calls.Add((nameof(GetAllAsync), null, cancellationToken)); return GetAll(cancellationToken); }
    public Task<Role?> FindByIdAsync(Id<Role> roleId, CancellationToken cancellationToken = default) { Calls.Add((nameof(FindByIdAsync), roleId, cancellationToken)); return FindById(roleId, cancellationToken); }
    public Task<Role?> FindByNameAsync(string roleName, CancellationToken cancellationToken = default) { Calls.Add((nameof(FindByNameAsync), roleName, cancellationToken)); return FindByName(roleName, cancellationToken); }
    public Task<List<Role>> GetByNamesAsync(IReadOnlyCollection<string> roleNames, CancellationToken cancellationToken = default) { Calls.Add((nameof(GetByNamesAsync), roleNames, cancellationToken)); return GetByNames(roleNames, cancellationToken); }
    public Task<bool> ExistsByNameAsync(string roleName, Id<Role>? excludeRoleId = null, CancellationToken cancellationToken = default) { Calls.Add((nameof(ExistsByNameAsync), (roleName, excludeRoleId), cancellationToken)); return ExistsByName(roleName, excludeRoleId, cancellationToken); }
    public Task<List<string>> GetRoleNamesByUserIdAsync(Id<User> userId, CancellationToken cancellationToken = default) { Calls.Add((nameof(GetRoleNamesByUserIdAsync), userId, cancellationToken)); return GetRoleNamesByUserId(userId, cancellationToken); }
    public Task<Dictionary<Id<User>, List<string>>> GetRoleNamesByUserIdsAsync(IReadOnlyCollection<Id<User>> userIds, CancellationToken cancellationToken = default) { Calls.Add((nameof(GetRoleNamesByUserIdsAsync), userIds, cancellationToken)); return GetRoleNamesByUserIds(userIds, cancellationToken); }
    public Task<List<string>> GetPermissionClaimsByUserIdAsync(Id<User> userId, CancellationToken cancellationToken = default) { Calls.Add((nameof(GetPermissionClaimsByUserIdAsync), userId, cancellationToken)); return GetPermissionClaimsByUserId(userId, cancellationToken); }
    public Task<List<string>> GetPermissionClaimsByRoleIdAsync(Id<Role> targetRoleId, CancellationToken cancellationToken = default) { Calls.Add((nameof(GetPermissionClaimsByRoleIdAsync), targetRoleId, cancellationToken)); return GetPermissionClaimsByRoleId(targetRoleId, cancellationToken); }
    public Task<Dictionary<Id<Role>, List<string>>> GetPermissionClaimsByRoleIdsAsync(IReadOnlyCollection<Id<Role>> targetRoleIds, CancellationToken cancellationToken = default) { Calls.Add((nameof(GetPermissionClaimsByRoleIdsAsync), targetRoleIds, cancellationToken)); return GetPermissionClaimsByRoleIds(targetRoleIds, cancellationToken); }
    public Task<bool> UserHasRoleAsync(Id<User> userId, string roleName, CancellationToken cancellationToken = default) { Calls.Add((nameof(UserHasRoleAsync), (userId, roleName), cancellationToken)); return UserHasRole(userId, roleName, cancellationToken); }
    public Task<bool> UserHasPermissionAsync(Id<User> userId, string permission, CancellationToken cancellationToken = default) { Calls.Add((nameof(UserHasPermissionAsync), (userId, permission), cancellationToken)); return UserHasPermission(userId, permission, cancellationToken); }
    public Task AddRoleAsync(Role role, CancellationToken cancellationToken = default) { Calls.Add((nameof(AddRoleAsync), role, cancellationToken)); return AddRole(role, cancellationToken); }
    public Task UpdateRoleAsync(Role role, CancellationToken cancellationToken = default) { Calls.Add((nameof(UpdateRoleAsync), role, cancellationToken)); return UpdateRole(role, cancellationToken); }
    public Task DeleteRoleAsync(Role role, CancellationToken cancellationToken = default) { Calls.Add((nameof(DeleteRoleAsync), role, cancellationToken)); return DeleteRole(role, cancellationToken); }
    public Task ReplaceRolePermissionClaimsAsync(Id<Role> targetRoleId, IReadOnlyCollection<string> permissionClaims, CancellationToken cancellationToken = default) { Calls.Add((nameof(ReplaceRolePermissionClaimsAsync), (targetRoleId, permissionClaims), cancellationToken)); return ReplaceRolePermissionClaims(targetRoleId, permissionClaims, cancellationToken); }
    public Task AddUserRolesAsync(Id<User> userId, IReadOnlyCollection<Id<Role>> roleIds, CancellationToken cancellationToken = default) { Calls.Add((nameof(AddUserRolesAsync), (userId, roleIds), cancellationToken)); return AddUserRoles(userId, roleIds, cancellationToken); }
    public Task ReplaceUserRolesAsync(Id<User> userId, IReadOnlyCollection<Id<Role>> roleIds, CancellationToken cancellationToken = default) { Calls.Add((nameof(ReplaceUserRolesAsync), (userId, roleIds), cancellationToken)); return ReplaceUserRoles(userId, roleIds, cancellationToken); }
    public Task<Pagination<Role>> GetRolesPaginatedAsync(FilterInput filterInput, CancellationToken cancellationToken = default) { Calls.Add((nameof(GetRolesPaginatedAsync), filterInput, cancellationToken)); return GetRolesPaginated(filterInput, cancellationToken); }
}
