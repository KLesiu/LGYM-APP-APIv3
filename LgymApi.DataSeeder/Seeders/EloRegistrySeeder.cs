using LgymApi.Domain.Entities;
using LgymApi.Domain.ValueObjects;
using LgymApi.Infrastructure.Data;
using Microsoft.EntityFrameworkCore;

namespace LgymApi.DataSeeder.Seeders;

public sealed class EloRegistrySeeder : IEntitySeeder
{
    public int Order => 1;

    public async Task SeedAsync(AppDbContext context, SeedContext seedContext, CancellationToken cancellationToken)
    {
        SeedOperationConsole.Start("Elo registry");
        var users = new List<User>();
        if (seedContext.AdminUser != null)
        {
            users.Add(seedContext.AdminUser);
        }

        if (seedContext.TesterUser != null)
        {
            users.Add(seedContext.TesterUser);
        }

        users.AddRange(seedContext.DemoUsers);

        if (users.Count == 0)
        {
            SeedOperationConsole.Skip("Elo registry");
            return;
        }

        var addedAny = false;
        foreach (var user in users.DistinctBy(u => u.Id))
        {
            var exists = await context.EloRegistries
                .AsNoTracking()
                .AnyAsync(entry => entry.UserId == user.Id, cancellationToken);
            if (exists)
            {
                continue;
            }

            var entry = new EloRegistry
            {
                Id = Id<EloRegistry>.New(),
                UserId = user.Id,
                Date = DateTimeOffset.UtcNow,
                Elo = 1000
            };

            await context.EloRegistries.AddAsync(entry, cancellationToken);
            seedContext.EloRegistries.Add(entry);
            addedAny = true;
        }

        if (!addedAny)
        {
            SeedOperationConsole.Skip("Elo registry");
            return;
        }

        SeedOperationConsole.Done("Elo registry");
    }
}
