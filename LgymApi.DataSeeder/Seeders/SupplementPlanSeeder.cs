using LgymApi.Domain.Entities;
using LgymApi.Domain.ValueObjects;
using LgymApi.Infrastructure.Data;
using Microsoft.EntityFrameworkCore;

namespace LgymApi.DataSeeder.Seeders;

public sealed class SupplementPlanSeeder : IEntitySeeder
{
    public int Order => 90;

    public async Task SeedAsync(AppDbContext context, SeedContext seedContext, CancellationToken cancellationToken)
    {
        SeedOperationConsole.Start("supplement plans");
        if (seedContext.SupplementPlans.Count > 0)
        {
            SeedOperationConsole.Skip("supplement plans");
            return;
        }

        var trainer = seedContext.DemoUsers.FirstOrDefault();
        var trainee = seedContext.DemoUsers.Skip(1).FirstOrDefault();

        if (trainer == null || trainee == null)
        {
            SeedOperationConsole.Skip("supplement plans");
            return;
        }

        var existing = await context.SupplementPlans
            .AsNoTracking()
            .Select(plan => new { plan.TrainerId, plan.TraineeId, plan.Name })
            .ToListAsync(cancellationToken);

        var existingSet = new HashSet<(Id<User> TrainerId, Id<User> TraineeId, string Name)>(
            existing.Select(entry => (entry.TrainerId, entry.TraineeId, entry.Name)));

        var plan = new SupplementPlan
        {
            Id = Id<SupplementPlan>.New(),
            TrainerId = trainer.Id,
            TraineeId = trainee.Id,
            Name = "Starter stack",
            Notes = "Basic supplements for onboarding",
            IsActive = true
        };

        if (!existingSet.Add((plan.TrainerId, plan.TraineeId, plan.Name)))
        {
            SeedOperationConsole.Skip("supplement plans");
            return;
        }

        await context.SupplementPlans.AddAsync(plan, cancellationToken);
        seedContext.SupplementPlans.Add(plan);
        SeedOperationConsole.Done("supplement plans");
    }
}
