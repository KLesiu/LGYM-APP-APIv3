using LgymApi.Domain.Entities;
using LgymApi.Domain.ValueObjects;
using LgymApi.Infrastructure.Data;
using Microsoft.EntityFrameworkCore;

namespace LgymApi.DataSeeder.Seeders;

public sealed class ExerciseTranslationSeeder : IEntitySeeder
{
    public int Order => 11;

    private static readonly IReadOnlyDictionary<string, string> PolishNames =
        new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
        {
            { "Bench Press", "Wyciskanie leżąc" },
            { "Back Squat", "Przysiad ze sztangą" },
            { "Deadlift", "Martwy ciąg" },
            { "Overhead Press", "Wyciskanie żołnierskie" },
            { "Barbell Row", "Wiosłowanie sztangą" },
            { "Pull-up", "Podciąganie na drążku" },
            { "Biceps Curl", "Uginanie ramion ze sztangą" },
            { "Triceps Extension", "Prostowanie ramion" }
        };

    public async Task SeedAsync(AppDbContext context, SeedContext seedContext, CancellationToken cancellationToken)
    {
        SeedOperationConsole.Start("exercise translations");
        var exercises = seedContext.Exercises;
        if (exercises.Count == 0)
        {
            SeedOperationConsole.Skip("exercise translations");
            return;
        }

        var addedAny = false;
        foreach (var exercise in exercises)
        {
            var existingCultures = await context.ExerciseTranslations
                .AsNoTracking()
                .Where(t => t.ExerciseId == exercise.Id)
                .Select(t => t.Culture)
                .ToListAsync(cancellationToken);

            if (!existingCultures.Contains("en", StringComparer.OrdinalIgnoreCase))
            {
                var translation = CreateTranslation(exercise.Id, "en", exercise.Name);
                await context.ExerciseTranslations.AddAsync(translation, cancellationToken);
                seedContext.ExerciseTranslations.Add(translation);
                addedAny = true;
            }

            if (!existingCultures.Contains("pl", StringComparer.OrdinalIgnoreCase))
            {
                var translation = CreateTranslation(exercise.Id, "pl", ResolvePolishName(exercise.Name));
                await context.ExerciseTranslations.AddAsync(translation, cancellationToken);
                seedContext.ExerciseTranslations.Add(translation);
                addedAny = true;
            }
        }

        if (!addedAny)
        {
            SeedOperationConsole.Skip("exercise translations");
            return;
        }

        SeedOperationConsole.Done("exercise translations");
    }

    private static ExerciseTranslation CreateTranslation(Id<Exercise> exerciseId, string culture, string name)
    {
        return new ExerciseTranslation
        {
            Id = Id<ExerciseTranslation>.New(),
            ExerciseId = exerciseId,
            Culture = culture,
            Name = name
        };
    }

    private static string ResolvePolishName(string name)
    {
        return PolishNames.TryGetValue(name, out var translated)
            ? translated
            : name;
    }
}
