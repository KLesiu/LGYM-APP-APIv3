using LgymApi.Application.Features.AdminManagement.Models;
using LgymApi.Application.Identity.Contracts.Ranking;
using LgymApi.Application.Pagination;
using LgymApi.Application.Repositories;
using LgymApi.Domain.Entities;
using LgymApi.Domain.ValueObjects;

namespace LgymApi.UnitTests.Fakes;

internal sealed class ConfigurableUserRepository : IUserRepository
{
    public List<(string Method, object? Argument, CancellationToken CancellationToken)> Calls { get; } = [];
    public Func<Id<User>, CancellationToken, Task<User?>> FindById { get; set; } = (_, _) => Task.FromResult<User?>(null);
    public Func<IReadOnlyCollection<Id<User>>, CancellationToken, Task<List<User>>> GetByIds { get; set; } = (_, _) => Task.FromResult(new List<User>());
    public Func<Id<User>, CancellationToken, Task<User?>> FindByIdIncludingDeleted { get; set; } = (_, _) => Task.FromResult<User?>(null);
    public Func<Id<User>, CancellationToken, Task<User?>> FindByIdWithRoles { get; set; } = (_, _) => Task.FromResult<User?>(null);
    public Func<string, CancellationToken, Task<User?>> FindByName { get; set; } = (_, _) => Task.FromResult<User?>(null);
    public Func<Email, CancellationToken, Task<User?>> FindByEmail { get; set; } = (_, _) => Task.FromResult<User?>(null);
    public Func<string, string, CancellationToken, Task<User?>> FindByNameOrEmail { get; set; } = (_, _, _) => Task.FromResult<User?>(null);
    public Func<CancellationToken, Task<List<RankingAccountProfile>>> GetRankingEligibleAccountProfiles { get; set; } = _ => Task.FromResult(new List<RankingAccountProfile>());
    public Func<User, CancellationToken, Task> Add { get; set; } = (_, _) => Task.CompletedTask;
    public Func<User, CancellationToken, Task> Update { get; set; } = (_, _) => Task.CompletedTask;
    public Func<FilterInput, bool, CancellationToken, Task<Pagination<UserResult>>> GetUsersPaginated { get; set; } = (_, _, _) => throw new NotSupportedException();

    public Task<User?> FindByIdAsync(Id<User> id, CancellationToken cancellationToken = default) { Calls.Add((nameof(FindByIdAsync), id, cancellationToken)); return FindById(id, cancellationToken); }
    public Task<List<User>> GetByIdsAsync(IReadOnlyCollection<Id<User>> ids, CancellationToken cancellationToken = default) { Calls.Add((nameof(GetByIdsAsync), ids, cancellationToken)); return GetByIds(ids, cancellationToken); }
    public Task<User?> FindByIdIncludingDeletedAsync(Id<User> id, CancellationToken cancellationToken = default) { Calls.Add((nameof(FindByIdIncludingDeletedAsync), id, cancellationToken)); return FindByIdIncludingDeleted(id, cancellationToken); }
    public Task<User?> FindByIdWithRolesAsync(Id<User> id, CancellationToken cancellationToken = default) { Calls.Add((nameof(FindByIdWithRolesAsync), id, cancellationToken)); return FindByIdWithRoles(id, cancellationToken); }
    public Task<User?> FindByNameAsync(string name, CancellationToken cancellationToken = default) { Calls.Add((nameof(FindByNameAsync), name, cancellationToken)); return FindByName(name, cancellationToken); }
    public Task<User?> FindByEmailAsync(Email email, CancellationToken cancellationToken = default) { Calls.Add((nameof(FindByEmailAsync), email, cancellationToken)); return FindByEmail(email, cancellationToken); }
    public Task<User?> FindByNameOrEmailAsync(string name, string email, CancellationToken cancellationToken = default) { Calls.Add((nameof(FindByNameOrEmailAsync), (name, email), cancellationToken)); return FindByNameOrEmail(name, email, cancellationToken); }
    public Task<List<RankingAccountProfile>> GetRankingEligibleAccountProfilesAsync(CancellationToken cancellationToken = default) { Calls.Add((nameof(GetRankingEligibleAccountProfilesAsync), null, cancellationToken)); return GetRankingEligibleAccountProfiles(cancellationToken); }
    public Task AddAsync(User user, CancellationToken cancellationToken = default) { Calls.Add((nameof(AddAsync), user, cancellationToken)); return Add(user, cancellationToken); }
    public Task UpdateAsync(User user, CancellationToken cancellationToken = default) { Calls.Add((nameof(UpdateAsync), user, cancellationToken)); return Update(user, cancellationToken); }
    public Task<Pagination<UserResult>> GetUsersPaginatedAsync(FilterInput filterInput, bool includeDeleted, CancellationToken cancellationToken = default) { Calls.Add((nameof(GetUsersPaginatedAsync), (filterInput, includeDeleted), cancellationToken)); return GetUsersPaginated(filterInput, includeDeleted, cancellationToken); }
}
