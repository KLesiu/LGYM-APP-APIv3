using LgymApi.Domain.Entities;
using LgymApi.Domain.Enums;
using LgymApi.Domain.ValueObjects;
using LgymApi.Infrastructure.Data;
using Microsoft.EntityFrameworkCore;

namespace LgymApi.DataSeeder.Seeders;

public sealed class TrainerInvitationSeeder : IEntitySeeder
{
    public int Order => 65;

    public async Task SeedAsync(AppDbContext context, SeedContext seedContext, CancellationToken cancellationToken)
    {
        SeedOperationConsole.Start("trainer invitations");
        if (seedContext.TrainerInvitations.Count > 0)
        {
            SeedOperationConsole.Skip("trainer invitations");
            return;
        }

        var demoUsers = seedContext.DemoUsers;
        if (demoUsers.Count < 2)
        {
            SeedOperationConsole.Skip("trainer invitations");
            return;
        }

        var trainer = demoUsers[0];
        var trainee = demoUsers[1];

        var existing = await context.TrainerInvitations
            .AsNoTracking()
            .Select(invite => new { invite.TrainerId, invite.TraineeId })
            .ToListAsync(cancellationToken);

        var existingSet = new HashSet<(Id<User> TrainerId, Id<User>? TraineeId)>(
            existing.Select(entry => (entry.TrainerId, entry.TraineeId)));

        var invitation = new TrainerInvitation
        {
            Id = Id<TrainerInvitation>.New(),
            TrainerId = trainer.Id,
            TraineeId = trainee.Id,
            Code = $"INV-{Id<TrainerInvitation>.New().ToString():N}"[..12].ToUpperInvariant(),
            Status = TrainerInvitationStatus.Pending,
            ExpiresAt = DateTimeOffset.UtcNow.AddDays(14)
        };

        if (!existingSet.Add((invitation.TrainerId, invitation.TraineeId)))
        {
            SeedOperationConsole.Skip("trainer invitations");
            return;
        }

        await context.TrainerInvitations.AddAsync(invitation, cancellationToken);
        seedContext.TrainerInvitations.Add(invitation);
        SeedOperationConsole.Done("trainer invitations");
    }
}
