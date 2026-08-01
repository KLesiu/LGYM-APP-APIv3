using LgymApi.Application.Mapping.Core;
using LgymApi.Application.Identity.Mapping;
using LgymApi.Application.Repositories;
using LgymApi.Application.Services;
using LgymApi.Domain.Entities;
using LgymApi.Domain.ValueObjects;
using LgymApi.Identity.Contracts;
using LgymApi.Identity.Contracts.Accounts;

namespace LgymApi.Application.Identity.Access;

internal sealed class AccountLookupService : IAccountLookupService
{
    private readonly IUserRepository _userRepository;
    private readonly IMapper _mapper;

    public AccountLookupService(IUserRepository userRepository, IMapper mapper)
    {
        _userRepository = userRepository;
        _mapper = mapper;
    }

    public async Task<AccountLookup?> GetByIdAsync(Id<AccountReference> accountId, CancellationToken cancellationToken = default)
    {
        var user = await _userRepository.FindByIdAsync(accountId.Rebind<User>(), cancellationToken);
        return user is { IsDeleted: false }
            ? _mapper.Map<User, AccountLookup>(user, _mapper.CreateContext())
            : null;
    }

    public async Task<AccountLookup?> GetByEmailAsync(string email, CancellationToken cancellationToken = default)
    {
        var user = await _userRepository.FindByEmailAsync(new Email(email), cancellationToken);
        return user is { IsDeleted: false }
            ? _mapper.Map<User, AccountLookup>(user, _mapper.CreateContext())
            : null;
    }

    public async Task<IReadOnlyList<AccountLookup>> GetByIdsAsync(
        IReadOnlyList<Id<AccountReference>> accountIds,
        CancellationToken cancellationToken = default)
    {
        var users = await _userRepository.GetByIdsAsync(
            accountIds.Select(accountId => accountId.Rebind<User>()).ToList(),
            cancellationToken);
        var accountsById = users
            .Where(user => !user.IsDeleted)
            .Select(user => _mapper.Map<User, AccountLookup>(user, _mapper.CreateContext()))
            .ToDictionary(account => account.Id);

        return accountIds
            .Where(accountsById.ContainsKey)
            .Select(accountId => accountsById[accountId])
            .ToList();
    }
}

internal sealed class AccountAccessReader : IAccountAccessReader
{
    private readonly IUserRepository _userRepository;
    private readonly IRoleRepository _roleRepository;
    private readonly IMapper _mapper;

    public AccountAccessReader(IUserRepository userRepository, IRoleRepository roleRepository, IMapper mapper)
    {
        _userRepository = userRepository;
        _roleRepository = roleRepository;
        _mapper = mapper;
    }

    public async Task<AccountAccessFacts?> GetByIdAsync(
        Id<AccountReference> accountId,
        CancellationToken cancellationToken = default)
    {
        var user = await _userRepository.FindByIdIncludingDeletedAsync(accountId.Rebind<User>(), cancellationToken);
        if (user is null)
        {
            return null;
        }

        var roles = await _roleRepository.GetRoleNamesByUserIdAsync(user.Id, cancellationToken);
        var permissionClaims = await _roleRepository.GetPermissionClaimsByUserIdAsync(user.Id, cancellationToken);
        var context = _mapper.CreateContext();
        context.Set(IdentityAccountContractMappingProfile.Keys.Roles, roles);
        context.Set(IdentityAccountContractMappingProfile.Keys.PermissionClaims, permissionClaims);
        return _mapper.Map<User, AccountAccessFacts>(user, context);
    }
}

internal sealed class AccountSessionValidator : IAccountSessionValidator
{
    private readonly IUserSessionStore _userSessionStore;

    public AccountSessionValidator(IUserSessionStore userSessionStore)
    {
        _userSessionStore = userSessionStore;
    }

    public Task<bool> IsValidAsync(Id<AccountSessionReference> sessionId, CancellationToken cancellationToken = default)
        => _userSessionStore.ValidateSessionAsync(sessionId.Rebind<UserSession>(), cancellationToken);
}

internal sealed class AuthenticatedAccountContextResolver : IAuthenticatedAccountContextResolver
{
    private readonly IAccountSessionValidator _sessionValidator;
    private readonly IAccountAccessReader _accountAccessReader;
    private readonly IMapper _mapper;

    public AuthenticatedAccountContextResolver(
        IAccountSessionValidator sessionValidator,
        IAccountAccessReader accountAccessReader,
        IMapper mapper)
    {
        _sessionValidator = sessionValidator;
        _accountAccessReader = accountAccessReader;
        _mapper = mapper;
    }

    public async Task<AuthenticatedAccountResolution> ResolveAsync(
        Id<AccountReference> accountId,
        Id<AccountSessionReference> sessionId,
        CancellationToken cancellationToken = default)
    {
        if (!await _sessionValidator.IsValidAsync(sessionId, cancellationToken))
        {
            return new AuthenticatedAccountResolution(AuthenticatedAccountResolutionStatus.SessionInvalid, null);
        }

        var accessFacts = await _accountAccessReader.GetByIdAsync(accountId, cancellationToken);
        if (accessFacts is null)
        {
            return new AuthenticatedAccountResolution(AuthenticatedAccountResolutionStatus.AccountNotFound, null);
        }

        var context = _mapper.CreateContext();
        context.Set(IdentityAccountContractMappingProfile.Keys.SessionId, sessionId);
        var authenticatedContext = _mapper.Map<AccountAccessFacts, AuthenticatedAccountContext>(accessFacts, context);

        var status = authenticatedContext.IsDeleted
            ? AuthenticatedAccountResolutionStatus.AccountDeleted
            : authenticatedContext.IsBlocked
                ? AuthenticatedAccountResolutionStatus.AccountBlocked
                : AuthenticatedAccountResolutionStatus.Active;
        return new AuthenticatedAccountResolution(status, authenticatedContext);
    }
}
