using LgymApi.Application.Features.AdminManagement.Models;
using LgymApi.Application.Identity.Contracts.Ranking;
using LgymApi.Application.Pagination;
using LgymApi.Application.Repositories;
using LgymApi.Domain.Entities;
using LgymApi.Domain.ValueObjects;
using LgymApi.Infrastructure.Data.SeedData;
using LgymApi.Infrastructure.Pagination;
using LgymApi.Identity.Persistence;
using Microsoft.EntityFrameworkCore;

namespace LgymApi.Infrastructure.Repositories;

internal sealed class UserRepository : IUserRepository
{
    private readonly IIdentityPersistenceContext _dbContext;
    private readonly IGridifyExecutionService _gridifyExecutionService;
    private readonly IMapperRegistry _mapperRegistry;

    private static readonly PaginationPolicy AdminUserPaginationPolicy = new()
    {
        MaxPageSize = 100,
        DefaultPageSize = 20,
        DefaultSortField = "name",
        TieBreakerField = "id"
    };

    public UserRepository(IIdentityPersistenceContext dbContext, IGridifyExecutionService gridifyExecutionService, IMapperRegistry mapperRegistry)
    {
        _dbContext = dbContext;
        _gridifyExecutionService = gridifyExecutionService;
        _mapperRegistry = mapperRegistry;
    }

    public Task<User?> FindByIdAsync(Id<User> id, CancellationToken cancellationToken = default)
    {
        return _dbContext.Users.FirstOrDefaultAsync(u => u.Id == id, cancellationToken);
    }

    public Task<List<User>> GetByIdsAsync(IReadOnlyCollection<Id<User>> ids, CancellationToken cancellationToken = default)
    {
        if (ids.Count == 0)
        {
            return Task.FromResult(new List<User>());
        }

        return _dbContext.Users
            .AsNoTracking()
            .Where(user => ids.Contains(user.Id))
            .ToListAsync(cancellationToken);
    }

    public Task<User?> FindByIdIncludingDeletedAsync(Id<User> id, CancellationToken cancellationToken = default)
    {
        return _dbContext.Users
            .IgnoreQueryFilters()
            .FirstOrDefaultAsync(u => u.Id == id, cancellationToken);
    }

    public Task<User?> FindByIdWithRolesAsync(Id<User> id, CancellationToken cancellationToken = default)
    {
        return _dbContext.Users
            .Include(u => u.UserRoles)
            .ThenInclude(ur => ur.Role)
            .ThenInclude(r => r.RoleClaims)
            .FirstOrDefaultAsync(u => u.Id == id, cancellationToken);
    }

    public Task<User?> FindByNameAsync(string name, CancellationToken cancellationToken = default)
    {
        return _dbContext.Users.AsNoTracking().FirstOrDefaultAsync(u => u.Name == name, cancellationToken);
    }

    public Task<User?> FindByEmailAsync(Email email, CancellationToken cancellationToken = default)
    {
        return _dbContext.Users.FirstOrDefaultAsync(u => u.Email == email, cancellationToken);
    }

    public Task<User?> FindByNameOrEmailAsync(string name, string email, CancellationToken cancellationToken = default)
    {
        return _dbContext.Users.AsNoTracking().FirstOrDefaultAsync(u => u.Name == name || u.Email == email, cancellationToken);
    }

    public async Task<List<RankingAccountProfile>> GetRankingEligibleAccountProfilesAsync(CancellationToken cancellationToken = default)
    {
        var profiles = await _dbContext.Users
            .AsNoTracking()
            .Where(user => !user.IsDeleted && user.IsVisibleInRanking)
            .Where(user => !user.UserRoles.Any(role => role.RoleId == RoleSeedDataConfiguration.TesterRoleSeedId))
            .Select(user => new
            {
                user.Id,
                user.Name,
                user.Avatar,
                user.ProfileRank
            })
            .ToListAsync(cancellationToken);

        return profiles.Select(user => new RankingAccountProfile(
            user.Id.Rebind<LgymApi.Identity.Contracts.AccountReference>(),
            user.Name,
            user.Avatar,
            user.ProfileRank)).ToList();
    }

    public Task AddAsync(User user, CancellationToken cancellationToken = default)
    {
        return _dbContext.Users.AddAsync(user, cancellationToken).AsTask();
    }

    public Task UpdateAsync(User user, CancellationToken cancellationToken = default)
    {
        _dbContext.Users.Update(user);
        return Task.CompletedTask;
    }

    public async Task<Pagination<UserResult>> GetUsersPaginatedAsync(FilterInput filterInput, bool includeDeleted, CancellationToken cancellationToken = default)
    {
        var baseQuery = BuildAdminUserBaseQuery(includeDeleted);

        var pagination = await _gridifyExecutionService.ExecuteAsync(
            baseQuery,
            filterInput,
            _mapperRegistry,
            AdminUserPaginationPolicy,
            cancellationToken);

        return new Pagination<UserResult>
        {
            Items = pagination.Items.Select(x => new UserResult
            {
                Id = x.Id,
                Name = x.Name,
                Email = x.Email,
                Avatar = x.Avatar,
                ProfileRank = x.ProfileRank,
                IsVisibleInRanking = x.IsVisibleInRanking,
                IsBlocked = x.IsBlocked,
                IsDeleted = x.IsDeleted,
                CreatedAt = x.CreatedAt
            }).ToList(),
            Page = pagination.Page,
            PageSize = pagination.PageSize,
            TotalCount = pagination.TotalCount
        };
    }

    private IQueryable<AdminUserProjection> BuildAdminUserBaseQuery(bool includeDeleted)
    {
        var query = _dbContext.Users.AsNoTracking();
        if (!includeDeleted)
        {
            query = query.Where(u => !u.IsDeleted);
        }

        return query.Select(u => new AdminUserProjection
        {
            Id = u.Id,
            Name = u.Name,
            Email = u.Email,
            Avatar = u.Avatar,
            ProfileRank = u.ProfileRank,
            IsVisibleInRanking = u.IsVisibleInRanking,
            IsBlocked = u.IsBlocked,
            IsDeleted = u.IsDeleted,
            CreatedAt = u.CreatedAt
        });
    }

    internal sealed class AdminUserProjection
    {
        public Id<User> Id { get; init; }
        public string Name { get; init; } = string.Empty;
        public string Email { get; init; } = string.Empty;
        public string? Avatar { get; init; }
        public string ProfileRank { get; init; } = string.Empty;
        public bool IsVisibleInRanking { get; init; }
        public bool IsBlocked { get; init; }
        public bool IsDeleted { get; init; }
        public DateTimeOffset CreatedAt { get; init; }
    }
}
