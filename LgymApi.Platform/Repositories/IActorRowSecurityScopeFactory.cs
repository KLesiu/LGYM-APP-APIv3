using LgymApi.Domain.ValueObjects;
using LgymApi.Platform.Contracts;

namespace LgymApi.Application.Repositories;

public interface IActorRowSecurityScopeFactory
{
    Task<IUnitOfWorkTransaction> BeginAsync(
        Id<ActorReference> actorId,
        CancellationToken cancellationToken = default);
}
