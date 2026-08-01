using LgymApi.Domain.Entities;
using LgymApi.Domain.Security;
using LgymApi.Domain.ValueObjects;
using LgymApi.Infrastructure.Data;
using LgymApi.Identity.Contracts;
using Microsoft.EntityFrameworkCore;

namespace LgymApi.DataSeeder.Seeders;

public sealed class RoleSeeder : IEntitySeeder
{
    public int Order => 2;

    public async Task SeedAsync(AppDbContext context, SeedContext seedContext, CancellationToken cancellationToken)
    {
        SeedOperationConsole.Start("roles");
        if (seedContext.Roles.Count > 0)
        {
            SeedOperationConsole.Skip("roles");
            return;
        }

        var existingNames = await context.Roles
            .AsNoTracking()
            .Select(role => role.Name)
            .ToListAsync(cancellationToken);

        var roles = new List<Role>
        {
            new()
            {
                Id = ParseSeedId<Role>(IdentitySeedIds.UserRole),
                Name = AuthConstants.Roles.User,
                Description = "Default role for all users"
            },
            new()
            {
                Id = ParseSeedId<Role>(IdentitySeedIds.AdminRole),
                Name = AuthConstants.Roles.Admin,
                Description = "Administrative privileges"
            },
            new()
            {
                Id = ParseSeedId<Role>(IdentitySeedIds.TesterRole),
                Name = AuthConstants.Roles.Tester,
                Description = "Excluded from ranking"
            },
            new()
            {
                Id = ParseSeedId<Role>(IdentitySeedIds.TrainerRole),
                Name = AuthConstants.Roles.Trainer,
                Description = "Trainer role for coach-facing APIs"
            }
        };

        var addedAny = false;
        foreach (var role in roles)
        {
            if (existingNames.Contains(role.Name, StringComparer.OrdinalIgnoreCase))
            {
                continue;
            }

            await context.Roles.AddAsync(role, cancellationToken);
            seedContext.Roles.Add(role);
            addedAny = true;
        }

        if (!addedAny)
        {
            SeedOperationConsole.Skip("roles");
            return;
        }

        SeedOperationConsole.Done("roles");
    }

    private static Id<TEntity> ParseSeedId<TEntity>(string value)
    {
        return Id<TEntity>.TryParse(value, out var id)
            ? id
            : throw new InvalidOperationException($"Invalid Identity seed ID '{value}'.");
    }
}
