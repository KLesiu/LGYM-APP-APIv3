using System.IdentityModel.Tokens.Jwt;
using System.Security.Claims;
using FluentAssertions;
using LgymApi.Domain.Entities;
using LgymApi.Domain.Security;
using LgymApi.Domain.ValueObjects;
using LgymApi.Infrastructure.Services;
using Microsoft.Extensions.Configuration;

namespace LgymApi.UnitTests;

[TestFixture]
public sealed class TokenServiceTests
{
    [Test]
    public void CreateToken_WithoutSigningKey_ThrowsInvalidOperationException()
    {
        var configuration = new ConfigurationBuilder().AddInMemoryCollection().Build();
        var service = new TokenService(configuration);

        var action = () => service.CreateToken(Id<User>.New(), Id<UserSession>.New(), Id<UserSession>.New().ToString(), Array.Empty<string>(), Array.Empty<string>());
        action.Should().Throw<InvalidOperationException>();
    }

    [Test]
    public void CreateToken_WithSigningKey_ReturnsSignedJwtWithAllClaims()
    {
        var settings = new Dictionary<string, string?>
        {
            [AuthConstants.ConfigKeys.JwtSigningKey] = "unit-test-signing-key-at-least-32-chars"
        };
        var configuration = new ConfigurationBuilder().AddInMemoryCollection(settings).Build();
        var service = new TokenService(configuration);

        var userId = Id<User>.New();
        var sessionId = Id<UserSession>.New();
        var jti = Id<UserSession>.New().ToString();
        var token = service.CreateToken(userId, sessionId, jti, ["User"], ["admin:access"]);

        token.Should().NotBeNullOrEmpty();

        var jwt = new JwtSecurityTokenHandler().ReadJwtToken(token);

        jwt.Claims.Any(c => c.Type == "sub" && c.Value == userId.ToString()).Should().BeTrue();
        jwt.Claims.Any(c => c.Type == AuthConstants.ClaimNames.UserId && c.Value == userId.ToString()).Should().BeTrue();
        jwt.Claims.Any(c => c.Type == AuthConstants.ClaimNames.SessionId && c.Value == sessionId.ToString()).Should().BeTrue();
        jwt.Claims.Any(c => c.Type == JwtRegisteredClaimNames.Jti && c.Value == jti).Should().BeTrue();
        jwt.Claims.Any(c => c.Type == ClaimTypes.Role && c.Value == "User").Should().BeTrue();
        jwt.Claims.Any(c => c.Type == "permission" && c.Value == "admin:access").Should().BeTrue();
    }
}
