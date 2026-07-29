using System.IdentityModel.Tokens.Jwt;
using System.Security.Claims;
using System.Text;
using LgymApi.Application.Services;
using LgymApi.Domain.Entities;
using LgymApi.Domain.Security;
using LgymApi.Domain.ValueObjects;
using Microsoft.Extensions.Configuration;
using Microsoft.IdentityModel.Tokens;

namespace LgymApi.Infrastructure.Services;

internal sealed class TokenService : ITokenService
{
    private readonly IConfiguration _configuration;

    public TokenService(IConfiguration configuration)
    {
        _configuration = configuration;
    }

    public string CreateToken(Id<User> userId, Id<UserSession> sessionId, string jti, IReadOnlyCollection<string> roles, IReadOnlyCollection<string> permissionClaims)
    {
        var signingKey = _configuration[AuthConstants.ConfigKeys.JwtSigningKey];
        if (string.IsNullOrWhiteSpace(signingKey) || signingKey.Length < 32)
        {
            throw new InvalidOperationException($"{AuthConstants.ConfigKeys.JwtSigningKey} is not configured or is too short. Set a strong key value.");
        }

        var key = new SymmetricSecurityKey(Encoding.UTF8.GetBytes(signingKey));
        var credentials = new SigningCredentials(key, SecurityAlgorithms.HmacSha256);

        var userIdString = userId.ToString();
        var claims = new List<Claim>
        {
            new(JwtRegisteredClaimNames.Sub, userIdString),
            new(AuthConstants.ClaimNames.UserId, userIdString),
            new(AuthConstants.ClaimNames.SessionId, sessionId.ToString()),
            new(JwtRegisteredClaimNames.Jti, jti)
        };

        foreach (var role in roles.Distinct(StringComparer.Ordinal))
        {
            claims.Add(new Claim(ClaimTypes.Role, role));
        }

        foreach (var permission in permissionClaims.Distinct(StringComparer.Ordinal))
        {
            claims.Add(new Claim(AuthConstants.PermissionClaimType, permission));
        }

        var token = new JwtSecurityToken(
            claims: claims,
            expires: DateTime.UtcNow.AddDays(30),
            signingCredentials: credentials);

        return new JwtSecurityTokenHandler().WriteToken(token);
    }
}
