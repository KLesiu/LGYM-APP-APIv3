using LgymApi.Domain.Entities;
using LgymApi.Domain.ValueObjects;
using LgymApi.Infrastructure.Data;
using Microsoft.EntityFrameworkCore;

namespace LgymApi.DataSeeder.Seeders;

public sealed class GymSeeder : IEntitySeeder
{
    public int Order => 21;

    public async Task SeedAsync(AppDbContext context, SeedContext seedContext, CancellationToken cancellationToken)
    {
        SeedOperationConsole.Start("gyms");
        if (seedContext.Gyms.Count > 0)
        {
            SeedOperationConsole.Skip("gyms");
            return;
        }

        var addresses = seedContext.Addresses;
        if (addresses.Count == 0)
        {
            SeedOperationConsole.Skip("gyms");
            return;
        }

        var demoUsers = seedContext.DemoUsers;
        if (demoUsers.Count == 0)
        {
            SeedOperationConsole.Skip("gyms");
            return;
        }

        var existing = await context.Gyms
            .AsNoTracking()
            .Select(gym => gym.Name)
            .ToListAsync(cancellationToken);

        var addedAny = false;
        for (var i = 0; i < demoUsers.Count; i++)
        {
            var user = demoUsers[i];
            var address = addresses[i % addresses.Count];
            var name = $"{user.Name} Gym";
            if (existing.Contains(name, StringComparer.OrdinalIgnoreCase))
            {
                continue;
            }

            var gym = new Gym
            {
                Id = Id<Gym>.New(),
                UserId = user.Id,
                Name = name,
                AddressId = address.Id
            };

            await context.Gyms.AddAsync(gym, cancellationToken);
            seedContext.Gyms.Add(gym);
            addedAny = true;
        }

        if (!addedAny)
        {
            SeedOperationConsole.Skip("gyms");
            return;
        }

        SeedOperationConsole.Done("gyms");
    }
}
