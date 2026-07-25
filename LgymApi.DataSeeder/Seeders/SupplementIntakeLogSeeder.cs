using LgymApi.Domain.Entities;
using LgymApi.Domain.ValueObjects;
using LgymApi.Infrastructure.Data;
using Microsoft.EntityFrameworkCore;

namespace LgymApi.DataSeeder.Seeders;

public sealed class SupplementIntakeLogSeeder : IEntitySeeder
{
    public int Order => 92;

    public async Task SeedAsync(AppDbContext context, SeedContext seedContext, CancellationToken cancellationToken)
    {
        SeedOperationConsole.Start("supplement intake logs");
        if (seedContext.SupplementIntakeLogs.Count > 0)
        {
            SeedOperationConsole.Skip("supplement intake logs");
            return;
        }

        var items = seedContext.SupplementPlanItems;
        var trainee = seedContext.DemoUsers.Skip(1).FirstOrDefault();
        if (items.Count == 0 || trainee == null)
        {
            SeedOperationConsole.Skip("supplement intake logs");
            return;
        }

        var existing = await context.SupplementIntakeLogs
            .AsNoTracking()
            .Select(log => new { log.TraineeId, log.PlanItemId, log.IntakeDate })
            .ToListAsync(cancellationToken);

        var existingSet = new HashSet<(Id<User> TraineeId, Id<SupplementPlanItem> PlanItemId, DateOnly IntakeDate)>(
            existing.Select(entry => (entry.TraineeId, entry.PlanItemId, entry.IntakeDate)));

        var addedAny = false;
        foreach (var item in items)
        {
            var intakeDate = DateOnly.FromDateTime(DateTime.UtcNow);
            if (!existingSet.Add((trainee.Id, item.Id, intakeDate)))
            {
                continue;
            }

            var log = new SupplementIntakeLog
            {
                Id = Id<SupplementIntakeLog>.New(),
                TraineeId = trainee.Id,
                PlanItemId = item.Id,
                IntakeDate = intakeDate,
                TakenAt = DateTimeOffset.UtcNow
            };

            await context.SupplementIntakeLogs.AddAsync(log, cancellationToken);
            seedContext.SupplementIntakeLogs.Add(log);
            addedAny = true;
        }

        if (!addedAny)
        {
            SeedOperationConsole.Skip("supplement intake logs");
            return;
        }

        SeedOperationConsole.Done("supplement intake logs");
    }
}
