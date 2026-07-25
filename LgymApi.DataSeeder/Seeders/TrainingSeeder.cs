using LgymApi.Domain.Entities;
using LgymApi.Domain.ValueObjects;
using LgymApi.Infrastructure.Data;
using Microsoft.EntityFrameworkCore;

namespace LgymApi.DataSeeder.Seeders;

public sealed class TrainingSeeder : IEntitySeeder
{
    public int Order => 40;

    public async Task SeedAsync(AppDbContext context, SeedContext seedContext, CancellationToken cancellationToken)
    {
        SeedOperationConsole.Start("trainings");
        if (seedContext.Trainings.Count > 0)
        {
            SeedOperationConsole.Skip("trainings");
            return;
        }

        var planDays = seedContext.PlanDays;
        var gyms = seedContext.Gyms;
        var users = seedContext.DemoUsers;
        if (planDays.Count == 0 || gyms.Count == 0 || users.Count == 0)
        {
            SeedOperationConsole.Skip("trainings");
            return;
        }

        var trainingIndex = 0;
        var existing = await context.Trainings
            .AsNoTracking()
            .Select(training => new { training.UserId, training.TypePlanDayId })
            .ToListAsync(cancellationToken);

        var existingSet = new HashSet<(Id<User> UserId, Id<PlanDay> TypePlanDayId)>(
            existing.Select(entry => (entry.UserId, entry.TypePlanDayId)));

        var addedAny = false;
        foreach (var user in users)
        {
            for (var i = 0; i < 3; i++)
            {
                var day = planDays[(trainingIndex + i) % planDays.Count];
                var gym = gyms[(trainingIndex + i) % gyms.Count];

                if (!existingSet.Add((user.Id, day.Id)))
                {
                    continue;
                }

                var training = new Training
                {
                    Id = Id<Training>.New(),
                    UserId = user.Id,
                    TypePlanDayId = day.Id,
                    GymId = gym.Id
                };

                await context.Trainings.AddAsync(training, cancellationToken);
                seedContext.Trainings.Add(training);
                addedAny = true;
            }

            trainingIndex++;
        }

        if (!addedAny)
        {
            SeedOperationConsole.Skip("trainings");
            return;
        }

        SeedOperationConsole.Done("trainings");
    }
}
