using System.IdentityModel.Tokens.Jwt;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Security.Claims;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using LgymApi.BackgroundWorker;
using LgymApi.Domain.Entities;
using LgymApi.Domain.Enums;
using LgymApi.Domain.Security;
using LgymApi.Domain.ValueObjects;
using LgymApi.Infrastructure.Data;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.IdentityModel.Tokens;

namespace LgymApi.IntegrationTests;

public abstract class PostgreSqlRecurringReportHttpTestBase : PostgreSqlIntegrationTestBase
{
    protected void SetAuthorizationHeader(Id<User> userId)
    {
        using var scope = Factory.Services.CreateScope();
        var database = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var session = new UserSession
        {
            Id = Id<UserSession>.New(),
            UserId = userId,
            Jti = Id<UserSession>.New().ToString(),
            ExpiresAtUtc = DateTimeOffset.UtcNow.AddDays(1)
        };
        database.UserSessions.Add(session);
        database.SaveChanges();

        var roles = database.UserRoles
            .Where(userRole => userRole.UserId == userId)
            .Select(userRole => userRole.Role.Name)
            .Distinct()
            .ToList();
        var permissions = database.UserRoles
            .Where(userRole => userRole.UserId == userId)
            .SelectMany(userRole => userRole.Role.RoleClaims)
            .Where(roleClaim => roleClaim.ClaimType == AuthConstants.PermissionClaimType)
            .Select(roleClaim => roleClaim.ClaimValue)
            .Distinct()
            .ToList();
        var claims = new List<Claim>
        {
            new(JwtRegisteredClaimNames.Sub, userId.ToString()),
            new("userId", userId.ToString()),
            new("sid", session.Id.ToString()),
            new(JwtRegisteredClaimNames.Jti, session.Jti)
        };
        claims.AddRange(roles.Select(role => new Claim(ClaimTypes.Role, role)));
        claims.AddRange(permissions.Select(permission =>
            new Claim(AuthConstants.PermissionClaimType, permission)));

        var key = new SymmetricSecurityKey(Encoding.UTF8.GetBytes(CustomWebApplicationFactory.TestJwtSigningKey));
        var token = new JwtSecurityToken(
            claims: claims,
            expires: DateTime.UtcNow.AddDays(1),
            signingCredentials: new SigningCredentials(key, SecurityAlgorithms.HmacSha256));
        Client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue(
            "Bearer",
            new JwtSecurityTokenHandler().WriteToken(token));
    }

    protected void SetIdempotencyKey(string key)
    {
        Client.DefaultRequestHeaders.Remove("Idempotency-Key");
        Client.DefaultRequestHeaders.Add("Idempotency-Key", key);
    }

    protected void ClearIdempotencyKey()
        => Client.DefaultRequestHeaders.Remove("Idempotency-Key");

    protected async Task<HttpResponseMessage> PostAsJsonWithApiOptionsAsync<T>(string requestUri, T value)
    {
        var hadIdempotencyKey = Client.DefaultRequestHeaders.Contains("Idempotency-Key");
        if (!hadIdempotencyKey)
        {
            SetIdempotencyKey($"test-auto-{requestUri.Replace("/", "-")}-{DateTime.UtcNow.Ticks:X16}");
        }

        var options = new JsonSerializerOptions
        {
            PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
            DictionaryKeyPolicy = JsonNamingPolicy.CamelCase,
            DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull
        };
        options.Converters.Add(new JsonStringEnumConverter(namingPolicy: null, allowIntegerValues: false));

        try
        {
            return await Client.PostAsJsonAsync(requestUri, value, options);
        }
        finally
        {
            if (!hadIdempotencyKey)
            {
                Client.DefaultRequestHeaders.Remove("Idempotency-Key");
            }
        }
    }

    protected async Task ProcessPendingCommandsAsync()
    {
        const int maxPasses = 5;
        for (var pass = 0; pass < maxPasses; pass++)
        {
            using var scope = Factory.Services.CreateScope();
            var database = scope.ServiceProvider.GetRequiredService<AppDbContext>();
            var orchestrator = scope.ServiceProvider.GetRequiredService<BackgroundActionOrchestratorService>();
            var envelopes = await database.CommandEnvelopes
                .Include(envelope => envelope.ExecutionLogs)
                .Where(envelope =>
                    envelope.Status != ActionExecutionStatus.Completed
                    && envelope.Status != ActionExecutionStatus.DeadLettered)
                .OrderBy(envelope => envelope.CreatedAt)
                .ToListAsync();
            if (envelopes.Count == 0)
            {
                return;
            }

            foreach (var envelope in envelopes.Where(envelope =>
                         envelope.Status == ActionExecutionStatus.Processing
                         && envelope.ExecutionLogs.All(log => log.ActionType != ActionExecutionLogType.Execute)))
            {
                envelope.Status = ActionExecutionStatus.Pending;
            }
            await database.SaveChangesAsync();

            var envelopeIds = envelopes
                .Where(envelope =>
                    envelope.Status == ActionExecutionStatus.Pending
                    || envelope.Status == ActionExecutionStatus.Failed)
                .Select(envelope => envelope.Id)
                .ToList();
            if (envelopeIds.Count == 0)
            {
                return;
            }

            foreach (var envelopeId in envelopeIds)
            {
                await orchestrator.OrchestrateAsync(envelopeId.ToString(), CancellationToken.None);
            }
        }
    }
}
