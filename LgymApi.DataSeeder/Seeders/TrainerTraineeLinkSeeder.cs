using LgymApi.Domain.Entities;
using LgymApi.Domain.ValueObjects;
using LgymApi.Infrastructure.Data;
using Microsoft.EntityFrameworkCore;

namespace LgymApi.DataSeeder.Seeders;

public sealed class TrainerTraineeLinkSeeder : IEntitySeeder
{
    public int Order => 66;

    public async Task SeedAsync(AppDbContext context, SeedContext seedContext, CancellationToken cancellationToken)
    {
        SeedOperationConsole.Start("trainer trainee links");
        if (seedContext.TrainerTraineeLinks.Count > 0)
        {
            SeedOperationConsole.Skip("trainer trainee links");
            return;
        }

        var demoUsers = seedContext.DemoUsers;
        if (demoUsers.Count < 2)
        {
            SeedOperationConsole.Skip("trainer trainee links");
            return;
        }

        var trainer = demoUsers[0];
        var trainee = demoUsers[1];

        var existing = await context.TrainerTraineeLinks
            .AsNoTracking()
            .Select(link => new { link.TrainerId, link.TraineeId })
            .ToListAsync(cancellationToken);

        var existingSet = new HashSet<(Id<User> TrainerId, Id<User> TraineeId)>(
            existing.Select(entry => (entry.TrainerId, entry.TraineeId)));

        var link = new TrainerTraineeLink
        {
            Id = Id<TrainerTraineeLink>.New(),
            TrainerId = trainer.Id,
            TraineeId = trainee.Id
        };

        if (!existingSet.Add((link.TrainerId, link.TraineeId)))
        {
            SeedOperationConsole.Skip("trainer trainee links");
            return;
        }

        await context.TrainerTraineeLinks.AddAsync(link, cancellationToken);
        seedContext.TrainerTraineeLinks.Add(link);
        SeedOperationConsole.Done("trainer trainee links");
    }
}
