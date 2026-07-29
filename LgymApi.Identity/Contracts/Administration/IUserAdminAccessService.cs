using LgymApi.Domain.ValueObjects;
using UserEntity = LgymApi.Domain.Entities.User;

namespace LgymApi.Application.Identity.Contracts.Administration;

public interface IUserAdminAccessService
{
    Task<bool> IsAdminAsync(Id<UserEntity> userId, CancellationToken cancellationToken = default);
}
