using LgymApi.Application.Features.AdminManagement.Models;
using LgymApi.Application.Features.AdminManagement.Models;
using LgymApi.Application.Identity.Contracts.Ranking;
using LgymApi.Application.Pagination;
using LgymApi.Domain.Entities;
using LgymApi.Domain.ValueObjects;

namespace LgymApi.Application.Repositories;

internal interface IUserRepository
{
    Task<User?> FindByIdAsync(Id<User> id, CancellationToken cancellationToken = default);
    Task<List<User>> GetByIdsAsync(IReadOnlyCollection<Id<User>> ids, CancellationToken cancellationToken = default)
        => throw new NotSupportedException();
    Task<User?> FindByIdIncludingDeletedAsync(Id<User> id, CancellationToken cancellationToken = default);
    Task<User?> FindByIdWithRolesAsync(Id<User> id, CancellationToken cancellationToken = default);
    Task<User?> FindByNameAsync(string name, CancellationToken cancellationToken = default);
    Task<User?> FindByEmailAsync(Email email, CancellationToken cancellationToken = default);
    Task<User?> FindByNameOrEmailAsync(string name, string email, CancellationToken cancellationToken = default);
    Task<List<RankingAccountProfile>> GetRankingEligibleAccountProfilesAsync(CancellationToken cancellationToken = default)
        => throw new NotSupportedException();
    Task AddAsync(User user, CancellationToken cancellationToken = default);
    Task UpdateAsync(User user, CancellationToken cancellationToken = default);
    Task<Pagination<UserResult>> GetUsersPaginatedAsync(FilterInput filterInput, bool includeDeleted, CancellationToken cancellationToken = default);
}
