using LgymApi.Application.Repositories;
using LgymApi.Domain.Entities;
using LgymApi.Domain.ValueObjects;

namespace LgymApi.UnitTests.Fakes;

internal sealed class ConfigurableUserExternalLoginRepository : IUserExternalLoginRepository
{
    public List<(string Method, object? Argument, CancellationToken CancellationToken)> Calls { get; } = [];
    public Func<UserExternalLogin, CancellationToken, Task> Add { get; set; } = (_, _) => Task.CompletedTask;
    public Func<string, string, CancellationToken, Task<UserExternalLogin?>> FindByProvider { get; set; } = (_, _, _) => Task.FromResult<UserExternalLogin?>(null);
    public Func<Id<User>, string, CancellationToken, Task<UserExternalLogin?>> FindByUserAndProvider { get; set; } = (_, _, _) => Task.FromResult<UserExternalLogin?>(null);
    public Func<Id<User>, CancellationToken, Task<UserExternalLogin?>> FindActiveGoogleByUserId { get; set; } = (_, _) => Task.FromResult<UserExternalLogin?>(null);
    public Func<Id<User>, CancellationToken, Task> MarkGoogleDeleted { get; set; } = (_, _) => Task.CompletedTask;
    public Func<Id<User>, CancellationToken, Task<List<UserExternalLogin>>> GetByUserId { get; set; } = (_, _) => Task.FromResult(new List<UserExternalLogin>());

    public Task AddAsync(UserExternalLogin externalLogin, CancellationToken cancellationToken = default) { Calls.Add((nameof(AddAsync), externalLogin, cancellationToken)); return Add(externalLogin, cancellationToken); }
    public Task<UserExternalLogin?> FindByProviderAsync(string provider, string providerKey, CancellationToken cancellationToken = default) { Calls.Add((nameof(FindByProviderAsync), (provider, providerKey), cancellationToken)); return FindByProvider(provider, providerKey, cancellationToken); }
    public Task<UserExternalLogin?> FindByUserAndProviderAsync(Id<User> userId, string provider, CancellationToken cancellationToken = default) { Calls.Add((nameof(FindByUserAndProviderAsync), (userId, provider), cancellationToken)); return FindByUserAndProvider(userId, provider, cancellationToken); }
    public Task<UserExternalLogin?> FindActiveGoogleByUserIdAsync(Id<User> userId, CancellationToken cancellationToken = default) { Calls.Add((nameof(FindActiveGoogleByUserIdAsync), userId, cancellationToken)); return FindActiveGoogleByUserId(userId, cancellationToken); }
    public Task MarkGoogleDeletedAsync(Id<User> userId, CancellationToken cancellationToken = default) { Calls.Add((nameof(MarkGoogleDeletedAsync), userId, cancellationToken)); return MarkGoogleDeleted(userId, cancellationToken); }
    public Task<List<UserExternalLogin>> GetByUserIdAsync(Id<User> userId, CancellationToken cancellationToken = default) { Calls.Add((nameof(GetByUserIdAsync), userId, cancellationToken)); return GetByUserId(userId, cancellationToken); }
}
