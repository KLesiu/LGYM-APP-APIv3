using LgymApi.Application.Services;
using LgymApi.Domain.Entities;
using LgymApi.Domain.ValueObjects;

namespace LgymApi.UnitTests.Fakes;

internal sealed class ConfigurableTokenService : ITokenService
{
    public List<(Id<User> UserId, Id<UserSession> SessionId, string Jti, IReadOnlyCollection<string> Roles, IReadOnlyCollection<string> PermissionClaims)> Calls { get; } = [];
    public Func<Id<User>, Id<UserSession>, string, IReadOnlyCollection<string>, IReadOnlyCollection<string>, string> Create { get; set; } = (_, _, _, _, _) => string.Empty;

    public string CreateToken(Id<User> userId, Id<UserSession> sessionId, string jti, IReadOnlyCollection<string> roles, IReadOnlyCollection<string> permissionClaims)
    {
        Calls.Add((userId, sessionId, jti, roles, permissionClaims));
        return Create(userId, sessionId, jti, roles, permissionClaims);
    }
}
