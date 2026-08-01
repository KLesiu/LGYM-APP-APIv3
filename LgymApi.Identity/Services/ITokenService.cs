using LgymApi.Domain.Entities;
using LgymApi.Domain.ValueObjects;

namespace LgymApi.Application.Services;

internal interface ITokenService
{
    string CreateToken(Id<User> userId, Id<UserSession> sessionId, string jti, IReadOnlyCollection<string> roles, IReadOnlyCollection<string> permissionClaims);
}
