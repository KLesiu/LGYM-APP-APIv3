using LgymApi.Application.Pagination;
using LgymApi.Application.Repositories;
using LgymApi.Domain.Entities;
using LgymApi.Domain.Security;
using LgymApi.Domain.ValueObjects;
using LgymApi.Infrastructure.Pagination;
using LgymApi.Identity.Persistence;
using Microsoft.EntityFrameworkCore;

namespace LgymApi.Infrastructure.Repositories;

internal sealed class RoleRepository : IRoleRepository
{
    private readonly IIdentityPersistenceContext _dbContext;
    private readonly IGridifyExecutionService _gridifyExecutionService;
    private readonly IMapperRegistry _mapperRegistry;

    private static readonly PaginationPolicy RolePaginationPolicy = new()
    {
        MaxPageSize = 100,
        DefaultPageSize = 20,
        DefaultSortField = "name",
        TieBreakerField = "id"
    };

    public RoleRepository(IIdentityPersistenceContext dbContext, IGridifyExecutionService gridifyExecutionService, IMapperRegistry mapperRegistry)
    {
        _dbContext = dbContext;
        _gridifyExecutionService = gridifyExecutionService;
        _mapperRegistry = mapperRegistry;
    }

    public Task<List<Role>> GetAllAsync(CancellationToken cancellationToken = default)
    {
        return _dbContext.Roles
            .AsNoTracking()
            .OrderBy(r => r.Name)
            .ToListAsync(cancellationToken);
    }

    public Task<Role?> FindByIdAsync(Id<Role> roleId, CancellationToken cancellationToken = default)
    {
        return _dbContext.Roles.FirstOrDefaultAsync(r => r.Id == roleId, cancellationToken);
    }

    public Task<Role?> FindByNameAsync(string roleName, CancellationToken cancellationToken = default)
    {
        return _dbContext.Roles
            .AsNoTracking()
            .FirstOrDefaultAsync(r => r.Name == roleName, cancellationToken);
    }

    public Task<bool> ExistsByNameAsync(string roleName, Id<Role>? excludeRoleId = null, CancellationToken cancellationToken = default)
    {
        var normalizedName = roleName.Trim();
        var query = _dbContext.Roles.AsQueryable();
        if (_dbContext.ProviderName?.Contains("Npgsql", StringComparison.OrdinalIgnoreCase) == true)
        {
            return query.AnyAsync(r =>
                EF.Functions.ILike(r.Name, normalizedName) &&
                (!excludeRoleId.HasValue || r.Id != excludeRoleId.Value),
            cancellationToken);
        }

        return query.AnyAsync(r =>
                string.Equals(r.Name, normalizedName, StringComparison.OrdinalIgnoreCase) &&
                (!excludeRoleId.HasValue || r.Id != excludeRoleId.Value),
            cancellationToken);
    }

    public Task<List<Role>> GetByNamesAsync(IReadOnlyCollection<string> roleNames, CancellationToken cancellationToken = default)
    {
        if (roleNames.Count == 0)
        {
            return Task.FromResult(new List<Role>());
        }

        var normalized = roleNames
            .Where(name => !string.IsNullOrWhiteSpace(name))
            .Select(name => name.Trim().ToLowerInvariant())
            .Distinct(StringComparer.Ordinal)
            .ToList();

        return _dbContext.Roles
            .AsNoTracking()
            .Where(r => normalized.Contains(r.Name.ToLower()))
            .ToListAsync(cancellationToken);
    }

    public Task<List<string>> GetRoleNamesByUserIdAsync(Id<User> userId, CancellationToken cancellationToken = default)
    {
        return _dbContext.UserRoles
            .AsNoTracking()
            .Where(ur => ur.UserId == userId)
            .Select(ur => ur.Role.Name)
            .Distinct()
            .OrderBy(name => name)
            .ToListAsync(cancellationToken);
    }

    public async Task<Dictionary<Id<User>, List<string>>> GetRoleNamesByUserIdsAsync(IReadOnlyCollection<Id<User>> userIds, CancellationToken cancellationToken = default)
    {
        if (userIds.Count == 0)
        {
            return new Dictionary<Id<User>, List<string>>();
        }

        var items = await _dbContext.UserRoles
            .AsNoTracking()
            .Where(ur => userIds.Contains(ur.UserId))
            .Select(ur => new { ur.UserId, RoleName = ur.Role.Name })
            .ToListAsync(cancellationToken);

        return items
            .GroupBy(i => i.UserId)
            .ToDictionary(
                g => g.Key,
                g => g.Select(i => i.RoleName)
                    .Distinct(StringComparer.Ordinal)
                    .OrderBy(v => v, StringComparer.Ordinal)
                    .ToList());
    }

    public Task<List<string>> GetPermissionClaimsByUserIdAsync(Id<User> userId, CancellationToken cancellationToken = default)
    {
        return _dbContext.UserRoles
            .AsNoTracking()
            .Where(ur => ur.UserId == userId)
            .SelectMany(ur => ur.Role.RoleClaims)
            .Where(rc => rc.ClaimType == AuthConstants.PermissionClaimType)
            .Select(rc => rc.ClaimValue)
            .Distinct()
            .OrderBy(value => value)
            .ToListAsync(cancellationToken);
    }

    public Task<List<string>> GetPermissionClaimsByRoleIdAsync(Id<Role> targetRoleId, CancellationToken cancellationToken = default)
    {
        return _dbContext.RoleClaims
            .AsNoTracking()
            .Where(rc => rc.RoleId == targetRoleId && rc.ClaimType == AuthConstants.PermissionClaimType)
            .Select(rc => rc.ClaimValue)
            .Distinct()
            .OrderBy(value => value)
            .ToListAsync(cancellationToken);
    }

    public async Task<Dictionary<Id<Role>, List<string>>> GetPermissionClaimsByRoleIdsAsync(IReadOnlyCollection<Id<Role>> targetRoleIds, CancellationToken cancellationToken = default)
    {
        if (targetRoleIds.Count == 0)
        {
            return new Dictionary<Id<Role>, List<string>>();
        }

        var items = await _dbContext.RoleClaims
            .AsNoTracking()
            .Where(rc => targetRoleIds.Contains(rc.RoleId) && rc.ClaimType == AuthConstants.PermissionClaimType)
            .Select(rc => new { rc.RoleId, rc.ClaimValue })
            .ToListAsync(cancellationToken);

        return items
            .GroupBy(i => i.RoleId)
            .ToDictionary(
                g => g.Key,
                g => g.Select(i => i.ClaimValue)
                    .Distinct(StringComparer.Ordinal)
                    .OrderBy(v => v, StringComparer.Ordinal)
                    .ToList());
    }

    public Task<bool> UserHasRoleAsync(Id<User> userId, string roleName, CancellationToken cancellationToken = default)
    {
        return _dbContext.UserRoles
            .AsNoTracking()
            .AnyAsync(ur => ur.UserId == userId && ur.Role.Name == roleName, cancellationToken);
    }

    public Task<bool> UserHasPermissionAsync(Id<User> userId, string permission, CancellationToken cancellationToken = default)
    {
        return _dbContext.UserRoles
            .AsNoTracking()
            .Where(ur => ur.UserId == userId)
            .SelectMany(ur => ur.Role.RoleClaims)
            .AnyAsync(rc => rc.ClaimType == AuthConstants.PermissionClaimType && rc.ClaimValue == permission, cancellationToken);
    }

    public async Task AddRoleAsync(Role role, CancellationToken cancellationToken = default)
    {
        await _dbContext.Roles.AddAsync(role, cancellationToken);
    }

    public Task UpdateRoleAsync(Role role, CancellationToken cancellationToken = default)
    {
        _dbContext.Roles.Update(role);
        return Task.CompletedTask;
    }

    public Task DeleteRoleAsync(Role role, CancellationToken cancellationToken = default)
    {
        _dbContext.Roles.Remove(role);
        return Task.CompletedTask;
    }

    public async Task ReplaceRolePermissionClaimsAsync(Id<Role> targetRoleId, IReadOnlyCollection<string> permissionClaims, CancellationToken cancellationToken = default)
    {
        var existingClaims = await _dbContext.RoleClaims
            .Where(rc => rc.RoleId == targetRoleId && rc.ClaimType == AuthConstants.PermissionClaimType)
            .ToListAsync(cancellationToken);

        if (existingClaims.Count > 0)
        {
            _dbContext.RoleClaims.RemoveRange(existingClaims);
        }

        var claimsToAdd = permissionClaims
            .Where(pc => !string.IsNullOrWhiteSpace(pc))
            .Select(pc => pc.Trim())
            .Distinct(StringComparer.Ordinal)
            .OrderBy(pc => pc, StringComparer.Ordinal)
            .Select(pc => new RoleClaim
            {
                Id = Id<RoleClaim>.New(),
                RoleId = targetRoleId,
                ClaimType = AuthConstants.PermissionClaimType,
                ClaimValue = pc
            })
            .ToList();

        if (claimsToAdd.Count > 0)
        {
            await _dbContext.RoleClaims.AddRangeAsync(claimsToAdd, cancellationToken);
        }

    }

    public async Task AddUserRolesAsync(Id<User> userId, IReadOnlyCollection<Id<Role>> roleIds, CancellationToken cancellationToken = default)
    {
        if (roleIds.Count == 0)
        {
            return;
        }

        var existingRoleIds = await _dbContext.UserRoles
            .Where(ur => ur.UserId == userId)
            .Select(ur => ur.RoleId)
            .ToListAsync(cancellationToken);

        var missing = roleIds
            .Where(roleId => !existingRoleIds.Contains(roleId))
            .Select(roleId => new UserRole { UserId = userId, RoleId = roleId })
            .ToList();

        if (missing.Count == 0)
        {
            return;
        }

        await _dbContext.UserRoles.AddRangeAsync(missing, cancellationToken);
    }

    public async Task ReplaceUserRolesAsync(Id<User> userId, IReadOnlyCollection<Id<Role>> roleIds, CancellationToken cancellationToken = default)
    {
        var existing = await _dbContext.UserRoles
            .Where(ur => ur.UserId == userId)
            .ToListAsync(cancellationToken);

        if (existing.Count > 0)
        {
            _dbContext.UserRoles.RemoveRange(existing);
        }

        if (roleIds.Count > 0)
        {
            var items = roleIds.Distinct().Select(roleId => new UserRole
            {
                UserId = userId,
                RoleId = roleId
            });
            await _dbContext.UserRoles.AddRangeAsync(items, cancellationToken);
        }
    }

    public async Task<Pagination<Role>> GetRolesPaginatedAsync(FilterInput filterInput, CancellationToken cancellationToken = default)
    {
        var baseQuery = _dbContext.Roles
            .AsNoTracking()
            .AsQueryable();

        return await _gridifyExecutionService.ExecuteAsync(
            baseQuery,
            filterInput,
            _mapperRegistry,
            RolePaginationPolicy,
            cancellationToken);
    }
}
