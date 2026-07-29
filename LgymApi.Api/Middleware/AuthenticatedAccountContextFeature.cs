using LgymApi.Identity.Contracts.Accounts;

namespace LgymApi.Api.Middleware;

public interface IAuthenticatedAccountContextFeature
{
    AuthenticatedAccountContext Context { get; }
}

internal sealed record AuthenticatedAccountContextFeature(AuthenticatedAccountContext Context) : IAuthenticatedAccountContextFeature;
