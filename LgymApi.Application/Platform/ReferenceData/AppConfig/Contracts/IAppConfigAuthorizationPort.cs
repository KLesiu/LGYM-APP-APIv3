using LgymApi.Domain.Entities;
using LgymApi.Domain.ValueObjects;

namespace LgymApi.Application.Platform.ReferenceData.AppConfig.Contracts;

public interface IAppConfigAuthorizationPort
{
    Task<bool> CanManageAppConfigAsync(
        Id<User> userId,
        CancellationToken cancellationToken = default);
}
