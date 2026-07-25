using LgymApi.Domain.Entities;
using LgymApi.Domain.Enums;
using LgymApi.Domain.ValueObjects;
using LgymApi.Infrastructure.Data;
using Microsoft.EntityFrameworkCore;

namespace LgymApi.DataSeeder.Seeders;

public sealed class MainRecordSeeder : IEntitySeeder
{
    public int Order => 51;

    public async Task SeedAsync(AppDbContext context, SeedContext seedContext, CancellationToken cancellationToken)
    {
        SeedOperationConsole.Start("main records");
        if (seedContext.MainRecords.Count > 0)
        {
            SeedOperationConsole.Skip("main records");
            return;
        }

        var users = seedContext.DemoUsers;
        var exercises = seedContext.Exercises;
        if (users.Count == 0 || exercises.Count == 0)
        {
            SeedOperationConsole.Skip("main records");
            return;
        }

        var existing = await context.MainRecords
            .AsNoTracking()
            .Select(record => new { record.UserId, record.ExerciseId })
            .ToListAsync(cancellationToken);

        var existingSet = new HashSet<(Id<User> UserId, Id<Exercise> ExerciseId)>(
            existing.Select(entry => (entry.UserId, entry.ExerciseId)));

        var recordIndex = 0;
        var addedAny = false;
        foreach (var user in users)
        {
            var exercise = exercises[recordIndex % exercises.Count];
            if (!existingSet.Add((user.Id, exercise.Id)))
            {
                recordIndex++;
                continue;
            }
            var record = new MainRecord
            {
                Id = Id<MainRecord>.New(),
                UserId = user.Id,
                ExerciseId = exercise.Id,
                Weight = new Weight(100 + recordIndex * 5, WeightUnits.Kilograms),
                Date = DateTimeOffset.UtcNow.AddDays(-recordIndex)
            };

            await context.MainRecords.AddAsync(record, cancellationToken);
            seedContext.MainRecords.Add(record);
            addedAny = true;
            recordIndex++;
        }

        if (!addedAny)
        {
            SeedOperationConsole.Skip("main records");
            return;
        }

        SeedOperationConsole.Done("main records");
    }
}
