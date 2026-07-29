using LgymApi.Domain.ValueObjects;
using UserEntity = LgymApi.Domain.Entities.User;

namespace LgymApi.Application.Identity.Contracts.Accounts;

public interface IAccountReadService
{
    Task<AccountReadModel?> GetByIdAsync(Id<UserEntity> accountId, CancellationToken cancellationToken = default);
    Task<AccountReadModel?> GetByEmailAsync(string email, CancellationToken cancellationToken = default);
    Task<IReadOnlyList<AccountReadModel>> GetByIdsAsync(
        IReadOnlyList<Id<UserEntity>> accountIds,
        CancellationToken cancellationToken = default);
}
