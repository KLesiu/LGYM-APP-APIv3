using LgymApi.Application.Identity.Contracts.Accounts;
using LgymApi.Application.Mapping.Core;
using LgymApi.Application.Repositories;
using LgymApi.Domain.ValueObjects;
using UserEntity = LgymApi.Domain.Entities.User;

namespace LgymApi.Application.Identity.Access;

internal sealed class AccountReadService : IAccountReadService
{
    private readonly IUserRepository _userRepository;
    private readonly IMapper _mapper;

    public AccountReadService(IUserRepository userRepository, IMapper mapper)
    {
        _userRepository = userRepository;
        _mapper = mapper;
    }

    public async Task<AccountReadModel?> GetByIdAsync(
        Id<UserEntity> accountId,
        CancellationToken cancellationToken = default)
    {
        var user = await _userRepository.FindByIdAsync(accountId, cancellationToken);
        return user is { IsDeleted: false }
            ? _mapper.Map<UserEntity, AccountReadModel>(user, _mapper.CreateContext())
            : null;
    }

    public async Task<AccountReadModel?> GetByEmailAsync(string email, CancellationToken cancellationToken = default)
    {
        var normalizedEmail = new Email(email);
        var user = await _userRepository.FindByEmailAsync(normalizedEmail, cancellationToken);
        return user is { IsDeleted: false }
            ? _mapper.Map<UserEntity, AccountReadModel>(user, _mapper.CreateContext())
            : null;
    }

    public async Task<IReadOnlyList<AccountReadModel>> GetByIdsAsync(
        IReadOnlyList<Id<UserEntity>> accountIds,
        CancellationToken cancellationToken = default)
    {
        var users = await _userRepository.GetByIdsAsync(accountIds, cancellationToken);
        var accountsById = users
            .Where(user => !user.IsDeleted)
            .Select(user => _mapper.Map<UserEntity, AccountReadModel>(user, _mapper.CreateContext()))
            .ToDictionary(account => account.Id);

        return accountIds
            .Where(accountsById.ContainsKey)
            .Select(accountId => accountsById[accountId])
            .ToList();
    }
}
