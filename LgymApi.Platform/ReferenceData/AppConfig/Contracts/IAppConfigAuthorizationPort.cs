using LgymApi.Domain.ValueObjects;
using LgymApi.Platform.Contracts;

namespace LgymApi.Application.Platform.ReferenceData.AppConfig.Contracts;

public interface IAppConfigAuthorizationPort
{
    Task<bool> CanManageAppConfigAsync(
        Id<ActorReference> actorId,
        CancellationToken cancellationToken = default);
}
