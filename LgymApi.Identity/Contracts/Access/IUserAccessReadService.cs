using LgymApi.Domain.ValueObjects;

namespace LgymApi.Application.Identity.Contracts.Access;

public interface IUserAccessReadService
{
    Task<bool> UserExistsAsync(Id<LgymApi.Domain.Entities.User> userId, CancellationToken cancellationToken = default);
    Task<bool> IsTrainerAsync(Id<LgymApi.Domain.Entities.User> userId, CancellationToken cancellationToken = default);
}
