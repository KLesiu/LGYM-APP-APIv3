using LgymApi.Domain.Entities;
using LgymApi.Domain.Enums;
using LgymApi.Domain.ValueObjects;
using LgymApi.Infrastructure.Data;
using Microsoft.EntityFrameworkCore;

namespace LgymApi.DataSeeder.Seeders;

public sealed class ExerciseSeeder : IEntitySeeder
{
    public int Order => 10;

    public async Task SeedAsync(AppDbContext context, SeedContext seedContext, CancellationToken cancellationToken)
    {
        SeedOperationConsole.Start("exercises");
        if (seedContext.Exercises.Count > 0)
        {
            SeedOperationConsole.Skip("exercises");
            return;
        }

        var exercises = new List<Exercise>
        {
            CreateExercise("Bench Press", BodyParts.Chest),
            CreateExercise("Back Squat", BodyParts.Quads),
            CreateExercise("Deadlift", BodyParts.Back),
            CreateExercise("Overhead Press", BodyParts.Shoulders),
            CreateExercise("Barbell Row", BodyParts.Back),
            CreateExercise("Pull-up", BodyParts.Back),
            CreateExercise("Biceps Curl", BodyParts.Biceps),
            CreateExercise("Triceps Extension", BodyParts.Triceps)
        };

        var existingNames = await context.Exercises
            .AsNoTracking()
            .Select(exercise => exercise.Name)
            .ToListAsync(cancellationToken);

        var addedAny = false;
        foreach (var exercise in exercises)
        {
            if (existingNames.Contains(exercise.Name, StringComparer.OrdinalIgnoreCase))
            {
                continue;
            }

            await context.Exercises.AddAsync(exercise, cancellationToken);
            seedContext.Exercises.Add(exercise);
            addedAny = true;
        }

        if (!addedAny)
        {
            SeedOperationConsole.Skip("exercises");
            return;
        }

        SeedOperationConsole.Done("exercises");
    }

    private static Exercise CreateExercise(string name, BodyParts bodyPart)
    {
        return new Exercise
        {
            Id = Id<Exercise>.New(),
            Name = name,
            BodyPart = bodyPart
        };
    }
}
