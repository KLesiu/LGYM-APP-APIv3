using LgymApi.Application.Repositories;
using LgymApi.Application.Services;
using LgymApi.Application.Identity.Contracts.Sessions;
using LgymApi.Domain.Entities;

namespace LgymApi.Application.Identity.Sessions;

internal sealed class UserSessionTerminationServiceDependencies
{
    public UserSessionTerminationServiceDependencies(
        IUserSessionStore userSessionStore,
        IAccountSessionDisassociationPort accountSessionDisassociationPort,
        IUnitOfWork unitOfWork)
    {
        UserSessionStore = userSessionStore;
        AccountSessionDisassociationPort = accountSessionDisassociationPort;
        UnitOfWork = unitOfWork;
    }

    public IUserSessionStore UserSessionStore { get; }
    public IAccountSessionDisassociationPort AccountSessionDisassociationPort { get; }
    public IUnitOfWork UnitOfWork { get; }
}
