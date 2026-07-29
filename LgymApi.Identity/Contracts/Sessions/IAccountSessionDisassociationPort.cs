using LgymApi.Domain.ValueObjects;
using LgymApi.Identity.Contracts;

namespace LgymApi.Application.Identity.Contracts.Sessions;

public interface IAccountSessionDisassociationPort
{
    Task StageDisassociateAsync(
        Id<AccountReference> accountId,
        Id<AccountSessionReference> sessionId,
        CancellationToken cancellationToken = default);
}
