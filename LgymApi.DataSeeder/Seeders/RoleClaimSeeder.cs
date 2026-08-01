using LgymApi.Domain.Entities;
using LgymApi.Domain.Security;
using LgymApi.Domain.ValueObjects;
using LgymApi.Infrastructure.Data;
using LgymApi.Identity.Contracts;
using Microsoft.EntityFrameworkCore;

namespace LgymApi.DataSeeder.Seeders;

public sealed class RoleClaimSeeder : IEntitySeeder
{
    public int Order => 3;

    public async Task SeedAsync(AppDbContext context, SeedContext seedContext, CancellationToken cancellationToken)
    {
        SeedOperationConsole.Start("role claims");
        if (seedContext.RoleClaims.Count > 0)
        {
            SeedOperationConsole.Skip("role claims");
            return;
        }

        var existing = await context.RoleClaims
            .AsNoTracking()
            .Select(claim => new { claim.RoleId, claim.ClaimType, claim.ClaimValue })
            .ToListAsync(cancellationToken);

        var existingSet = new HashSet<(Id<Role> RoleId, string ClaimType, string ClaimValue)>(
            existing.Select(entry => (entry.RoleId, entry.ClaimType, entry.ClaimValue)));

        var claims = new List<RoleClaim>
        {
            new()
            {
                Id = ParseSeedId<RoleClaim>(IdentitySeedIds.AdminAccessClaim),
                RoleId = ParseSeedId<Role>(IdentitySeedIds.AdminRole),
                ClaimType = AuthConstants.PermissionClaimType,
                ClaimValue = AuthConstants.Permissions.AdminAccess
            },
            new()
            {
                Id = ParseSeedId<RoleClaim>(IdentitySeedIds.ManageUserRolesClaim),
                RoleId = ParseSeedId<Role>(IdentitySeedIds.AdminRole),
                ClaimType = AuthConstants.PermissionClaimType,
                ClaimValue = AuthConstants.Permissions.ManageUserRoles
            },
            new()
            {
                Id = ParseSeedId<RoleClaim>(IdentitySeedIds.ManageAppConfigClaim),
                RoleId = ParseSeedId<Role>(IdentitySeedIds.AdminRole),
                ClaimType = AuthConstants.PermissionClaimType,
                ClaimValue = AuthConstants.Permissions.ManageAppConfig
            },
            new()
            {
                Id = ParseSeedId<RoleClaim>(IdentitySeedIds.ManageGlobalExercisesClaim),
                RoleId = ParseSeedId<Role>(IdentitySeedIds.AdminRole),
                ClaimType = AuthConstants.PermissionClaimType,
                ClaimValue = AuthConstants.Permissions.ManageGlobalExercises
            }
        };

        var addedAny = false;
        foreach (var claim in claims)
        {
            if (!existingSet.Add((claim.RoleId, claim.ClaimType, claim.ClaimValue)))
            {
                continue;
            }

            await context.RoleClaims.AddAsync(claim, cancellationToken);
            seedContext.RoleClaims.Add(claim);
            addedAny = true;
        }

        if (!addedAny)
        {
            SeedOperationConsole.Skip("role claims");
            return;
        }

        SeedOperationConsole.Done("role claims");
    }

    private static Id<TEntity> ParseSeedId<TEntity>(string value)
    {
        return Id<TEntity>.TryParse(value, out var id)
            ? id
            : throw new InvalidOperationException($"Invalid Identity seed ID '{value}'.");
    }
}
