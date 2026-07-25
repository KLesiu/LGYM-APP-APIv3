using LgymApi.Domain.Entities;
using LgymApi.Domain.ValueObjects;
using LgymApi.Infrastructure.Data;
using Microsoft.EntityFrameworkCore;

namespace LgymApi.DataSeeder.Seeders;

public sealed class PlanDayExerciseSeeder : IEntitySeeder
{
    public int Order => 32;

    public async Task SeedAsync(AppDbContext context, SeedContext seedContext, CancellationToken cancellationToken)
    {
        SeedOperationConsole.Start("plan day exercises");
        if (seedContext.PlanDayExercises.Count > 0)
        {
            SeedOperationConsole.Skip("plan day exercises");
            return;
        }

        var planDays = seedContext.PlanDays;
        var exercises = seedContext.Exercises;
        if (planDays.Count == 0 || exercises.Count == 0)
        {
            SeedOperationConsole.Skip("plan day exercises");
            return;
        }

        var existingPairs = await context.PlanDayExercises
            .AsNoTracking()
            .Select(entry => new { entry.PlanDayId, entry.ExerciseId })
            .ToListAsync(cancellationToken);

        var existingSet = new HashSet<(Id<PlanDay> PlanDayId, Id<Exercise> ExerciseId)>(
            existingPairs.Select(pair => (pair.PlanDayId, pair.ExerciseId)));

        var index = 0;
        var addedAny = false;
        foreach (var day in planDays)
        {
            var dayExercises = exercises.Skip(index).Take(4).ToList();
            if (dayExercises.Count == 0)
            {
                dayExercises = exercises.Take(4).ToList();
            }

            var order = 1;
            foreach (var exercise in dayExercises)
            {
                if (!existingSet.Add((day.Id, exercise.Id)))
                {
                    order++;
                    continue;
                }

                var entry = new PlanDayExercise
                {
                    Id = Id<PlanDayExercise>.New(),
                    PlanDayId = day.Id,
                    ExerciseId = exercise.Id,
                    Order = order,
                    Series = 3,
                    Reps = "8-12"
                };

                await context.PlanDayExercises.AddAsync(entry, cancellationToken);
                seedContext.PlanDayExercises.Add(entry);
                addedAny = true;
                order++;
            }

            index += 2;
        }

        if (!addedAny)
        {
            SeedOperationConsole.Skip("plan day exercises");
            return;
        }

        SeedOperationConsole.Done("plan day exercises");
    }
}
