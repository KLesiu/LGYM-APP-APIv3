using LgymApi.Domain.Entities;
using LgymApi.Domain.ValueObjects;
using LgymApi.Infrastructure.Data;
using Microsoft.EntityFrameworkCore;

namespace LgymApi.DataSeeder.Seeders;

public sealed class PlanDaySeeder : IEntitySeeder
{
    public int Order => 31;

    public async Task SeedAsync(AppDbContext context, SeedContext seedContext, CancellationToken cancellationToken)
    {
        SeedOperationConsole.Start("plan days");
        if (seedContext.PlanDays.Count > 0)
        {
            SeedOperationConsole.Skip("plan days");
            return;
        }

        var plans = seedContext.Plans;
        if (plans.Count == 0)
        {
            SeedOperationConsole.Skip("plan days");
            return;
        }

        var existing = await context.PlanDays
            .AsNoTracking()
            .Select(day => new { day.PlanId, day.Name })
            .ToListAsync(cancellationToken);

        var dayNames = new[] { "Push", "Pull", "Legs" };
        var addedAny = false;
        foreach (var plan in plans)
        {
            foreach (var name in dayNames)
            {
                if (existing.Any(day => day.PlanId == plan.Id && day.Name == name))
                {
                    continue;
                }

                var day = new PlanDay
                {
                    Id = Id<PlanDay>.New(),
                    PlanId = plan.Id,
                    Name = name
                };

                await context.PlanDays.AddAsync(day, cancellationToken);
                seedContext.PlanDays.Add(day);
                addedAny = true;
            }
        }

        if (!addedAny)
        {
            SeedOperationConsole.Skip("plan days");
            return;
        }

        SeedOperationConsole.Done("plan days");
    }
}
