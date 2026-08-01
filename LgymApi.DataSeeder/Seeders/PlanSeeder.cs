using LgymApi.Domain.Entities;
using LgymApi.Domain.ValueObjects;
using LgymApi.Infrastructure.Data;
using Microsoft.EntityFrameworkCore;

namespace LgymApi.DataSeeder.Seeders;

public sealed class PlanSeeder : IEntitySeeder
{
    public int Order => 30;

    public async Task SeedAsync(AppDbContext context, SeedContext seedContext, CancellationToken cancellationToken)
    {
        SeedOperationConsole.Start("plans");
        if (seedContext.Plans.Count > 0)
        {
            SeedOperationConsole.Skip("plans");
            return;
        }

        var demoUsers = seedContext.DemoUsers;
        if (demoUsers.Count == 0)
        {
            SeedOperationConsole.Skip("plans");
            return;
        }

        var existing = await context.Plans
            .AsNoTracking()
            .Select(plan => new { plan.UserId, plan.Name })
            .ToListAsync(cancellationToken);

        var addedAny = false;
        foreach (var user in demoUsers)
        {
            if (existing.Any(plan => plan.UserId == user.Id && plan.Name == "Push Pull Legs"))
            {
                continue;
            }

            var plan = new Plan
            {
                Id = Id<Plan>.New(),
                UserId = user.Id,
                Name = "Push Pull Legs",
                IsActive = true
            };

            await context.Plans.AddAsync(plan, cancellationToken);
            seedContext.Plans.Add(plan);
            addedAny = true;
        }

        if (!addedAny)
        {
            SeedOperationConsole.Skip("plans");
            return;
        }

        SeedOperationConsole.Done("plans");
    }
}
