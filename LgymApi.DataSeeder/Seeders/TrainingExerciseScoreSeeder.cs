using LgymApi.Domain.Entities;
using LgymApi.Domain.ValueObjects;
using LgymApi.Infrastructure.Data;
using Microsoft.EntityFrameworkCore;

namespace LgymApi.DataSeeder.Seeders;

public sealed class TrainingExerciseScoreSeeder : IEntitySeeder
{
    public int Order => 42;

    public async Task SeedAsync(AppDbContext context, SeedContext seedContext, CancellationToken cancellationToken)
    {
        SeedOperationConsole.Start("training exercise scores");
        if (seedContext.TrainingExerciseScores.Count > 0)
        {
            SeedOperationConsole.Skip("training exercise scores");
            return;
        }

        var trainings = seedContext.Trainings;
        var exerciseScores = seedContext.ExerciseScores;
        if (trainings.Count == 0 || exerciseScores.Count == 0)
        {
            SeedOperationConsole.Skip("training exercise scores");
            return;
        }

        var existingPairs = await context.TrainingExerciseScores
            .AsNoTracking()
            .Select(entry => new { entry.TrainingId, entry.ExerciseScoreId })
            .ToListAsync(cancellationToken);

        var existingSet = new HashSet<(Id<Training> TrainingId, Id<ExerciseScore> ExerciseScoreId)>(
            existingPairs.Select(pair => (pair.TrainingId, pair.ExerciseScoreId)));

        var trainingOrderMap = new Dictionary<Id<Training>, int>();
        var index = 0;
        var addedAny = false;
        foreach (var score in exerciseScores)
        {
            var training = trainings[index % trainings.Count];
            if (!existingSet.Add((training.Id, score.Id)))
            {
                index++;
                continue;
            }

            // Initialize per-training counter if not exists
            if (!trainingOrderMap.ContainsKey(training.Id))
            {
                trainingOrderMap[training.Id] = 0;
            }

            var entry = new TrainingExerciseScore
            {
                Id = Id<TrainingExerciseScore>.New(),
                TrainingId = training.Id,
                ExerciseScoreId = score.Id,
                Order = trainingOrderMap[training.Id]
            };

            trainingOrderMap[training.Id]++;
            await context.TrainingExerciseScores.AddAsync(entry, cancellationToken);
            seedContext.TrainingExerciseScores.Add(entry);
            addedAny = true;
            index++;
        }

        if (!addedAny)
        {
            SeedOperationConsole.Skip("training exercise scores");
            return;
        }

        SeedOperationConsole.Done("training exercise scores");
    }

}
