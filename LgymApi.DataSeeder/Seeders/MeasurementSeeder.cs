using LgymApi.Domain.Entities;
using LgymApi.Domain.Enums;
using LgymApi.Domain.ValueObjects;
using LgymApi.Infrastructure.Data;
using Microsoft.EntityFrameworkCore;

namespace LgymApi.DataSeeder.Seeders;

public sealed class MeasurementSeeder : IEntitySeeder
{
    public int Order => 50;

    public async Task SeedAsync(AppDbContext context, SeedContext seedContext, CancellationToken cancellationToken)
    {
        SeedOperationConsole.Start("measurements");
        if (seedContext.Measurements.Count > 0)
        {
            SeedOperationConsole.Skip("measurements");
            return;
        }

        var users = seedContext.DemoUsers;
        if (users.Count == 0)
        {
            SeedOperationConsole.Skip("measurements");
            return;
        }

        var existing = await context.Measurements
            .AsNoTracking()
            .Select(entry => new { entry.UserId, entry.BodyPart })
            .ToListAsync(cancellationToken);

        var existingSet = new HashSet<(Id<User> UserId, BodyParts BodyPart)>(
            existing.Select(entry => (entry.UserId, entry.BodyPart)));

        var bodyParts = new[] { BodyParts.Chest, BodyParts.Biceps, BodyParts.Abs };
        var measurementIndex = 0;
        var addedAny = false;
        foreach (var user in users)
        {
            foreach (var part in bodyParts)
            {
                if (!existingSet.Add((user.Id, part)))
                {
                    measurementIndex++;
                    continue;
                }
                var measurement = new Measurement
                {
                    Id = Id<Measurement>.New(),
                    UserId = user.Id,
                    BodyPart = part,
                    Unit = "cm",
                    Value = 80 + measurementIndex * 2
                };

                await context.Measurements.AddAsync(measurement, cancellationToken);
                seedContext.Measurements.Add(measurement);
                addedAny = true;
                measurementIndex++;
            }
        }

        if (!addedAny)
        {
            SeedOperationConsole.Skip("measurements");
            return;
        }

        SeedOperationConsole.Done("measurements");
    }
}
